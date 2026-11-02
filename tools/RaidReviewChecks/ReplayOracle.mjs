import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
import fs from 'node:fs';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';

const source = path.resolve(process.argv[2], 'Private/src/helpers').replaceAll('\\', '/') + '/';
const { finalPlayerStatuses } = await import(pathToFileURL(source + 'playerStatuses.ts'));
const { playerEndTimes } = await import(pathToFileURL(source + 'playerPresence.ts'));
const { isHumanPlayer, playerGroupKey } = await import(pathToFileURL(source + 'playerIdentity.ts'));
let checks = 0;
function check(actual, expected) { assert.deepEqual(actual, expected); checks++; }

// Positive reproduction control: reusing the profile ID makes later life invisible.
const legacyStatuses = [{ profileId: 'pmc', time: 0, status: 'Alive' }, { profileId: 'pmc', time: 1000, status: 'Dead' }, { profileId: 'pmc', time: 2000, status: 'Alive' }];
check(finalPlayerStatuses(legacyStatuses).get('pmc'), 'Dead');
check(playerEndTimes({ pmc: [{ time: 0 }, { time: 3000 }] }, legacyStatuses, [{ killedId: 'pmc', time: 1000 }]).pmc, 1000);

const owner = 'pmc';
const first = 'pmc:redeploy:1', second = 'pmc:redeploy:2', scav = 'scav:redeploy:3';
const statuses = [
    { profileId: first, time: 0, status: 'Alive' },
    { profileId: first, time: 1000, status: 'Dead' },
    { profileId: second, time: 2000, status: 'Alive' },
    { profileId: second, time: 3000, status: 'Dead' },
    { profileId: scav, time: 4000, status: 'Alive' },
    { profileId: scav, time: 5000, status: 'Extracted' },
];
const positions = { [first]: [{ time: 0 }, { time: 990 }], [second]: [{ time: 2000 }, { time: 2990 }], [scav]: [{ time: 4000 }, { time: 4990 }] };
const kills = [{ killedId: first, time: 1000 }, { killedId: second, time: 3000 }];
const final = finalPlayerStatuses(statuses);
const ends = playerEndTimes(positions, statuses, kills);
check(final.get(first), 'Dead');
check(final.get(second), 'Dead');
check(final.get(scav), 'Extracted');
check(ends[first], 990); // Upstream additionally bounds old tracks to their last captured position.
check(ends[second], 3000);
check(ends[scav], 5000);
for (const id of [first, second, scav]) {
    const participant = { profileId: id, type: 'HUMAN', mod_SAIN_brain: 'PLAYER', group: 0 };
    check(isHumanPlayer(participant, owner), true);
    check(playerGroupKey(participant, owner), 'players');
    check(participant.profileId === owner, false); // RaidOverview result override cannot rewrite an old life.
}
// Consume packets emitted by the production-linked adapter runtime, not a second mapper.
const trace = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
const sent = action => trace.filter(p => p.action === action).map(p => p.snapshot);
const start = sent('START')[0], finish = sent('END')[0];
check(sent('START').length, 1); check(sent('END').length, 1);
check(start.profileId, finish.profileId); check(start.sessionId, finish.sessionId);
const participants = sent('PLAYER');
check([...new Set(participants.map(p => p.profileId))], [first, second, scav]);
const terminal = finalPlayerStatuses(sent('PLAYER_STATUS'));
check(terminal.get(first), 'Dead'); check(terminal.get(second), 'Dead'); check(terminal.get(scav), 'Extracted');
for (const p of participants) {
    check(isHumanPlayer(p, start.profileId), true);
    check(playerGroupKey(p, start.profileId), 'players');
    check(p.profileId === start.profileId, false);
    assert.match(p.name, /\[life [123]\]$/); checks++;
}
check(sent('BALLISTIC').some(p => p.profileId === second && p.hitPlayerId === first), true);
check(sent('BALLISTIC').some(p => p.profileId === first), true);
check(sent('KILL').some(p => p.profileId === first && p.killedId === second), true);
check(sent('LOOT').find(p => p.itemId === 'drop-before-respawn').profileId, first);
check(sent('LOOT').find(p => p.itemId === 'real-loot').profileId, second);
check(sent('PLAYER_INVENTORY').map(p => p.profileId), [first, second, scav]);
for (const p of trace) {
    if (['START', 'END'].includes(p.action)) continue;
    for (const row of Array.isArray(p.snapshot) ? p.snapshot : [p.snapshot]) {
        if (row.profileId === 'pmc' || row.profileId === 'scav') throw new Error('real participant ID leaked into projected trace');
    }
}
checks++;

// Storage-boundary fixtures: independent rows survive profile+raid deduplication and
// CSV string IDs survive a round trip. This does not claim to run RR's webserver.
const db = new DatabaseSync(':memory:');
db.exec('CREATE TABLE raids (id TEXT PRIMARY KEY, owner TEXT); CREATE TABLE players (raid TEXT, id TEXT, name TEXT, UNIQUE(raid,id));');
db.prepare('INSERT INTO raids VALUES (?,?)').run(start.sessionId, start.profileId);
for (const p of participants) db.prepare('INSERT OR IGNORE INTO players VALUES (?,?,?)').run(start.sessionId, p.profileId, p.name);
check(db.prepare('SELECT COUNT(*) AS n FROM players').get().n, 3);
check(db.prepare('SELECT owner FROM raids').get().owner, 'pmc-owner');
check(db.prepare('SELECT id FROM players WHERE raid=? ORDER BY name').all(start.sessionId).map(p => p.id), [first, second, scav]);
const rows = sent('PLAYER_STATUS').map(p => [p.profileId, p.time, p.status].join(','));
const csv = 'profileId,time,status\n' + rows.join('\n');
const replayed = csv.split('\n').slice(1).map(line => { const [profileId,time,status] = line.split(','); return { profileId,time,status }; });
check([...finalPlayerStatuses(replayed)], [...terminal]);
fs.writeFileSync(path.join(path.dirname(process.argv[3]), 'status-fixture.csv'), csv);
db.close();
console.log(`RAID_REVIEW_REPLAY_PASSED ${checks} assertions against pinned upstream 1.10.0 helpers + emitted packets + SQLite/CSV fixtures`);
