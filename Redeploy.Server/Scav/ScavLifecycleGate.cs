using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;

namespace Redeploy.Server.Scav;

public sealed partial class ScavContinuationService
{
    public async Task<AccountOperationLease> AcquireRaidStart(MongoId sessionId, StartLocalRaidRequestData request, CancellationToken ct)
    {
        string id = sessionId.ToString();
        await Gate(id).WaitAsync(ct);
        try
        {
            await SaveGate(id).WaitAsync(ct);
            try { BeforeStartCore(sessionId); }
            finally { SaveGate(id).Release(); }
            return new AccountOperationLease(this, sessionId, request);
        }
        catch { Gate(id).Release(); throw; }
    }

    /// <summary>Transfers an account lock from one native prefix to completion of
    /// its original Task. No recursive call can repeat another mod's prefix.</summary>
    public sealed class AccountOperationLease(ScavContinuationService owner, MongoId sessionId, StartLocalRaidRequestData? request) : IDisposable
    {
        private ScavContinuationService? heldOwner = owner;
        private bool taskOwnsLease;

        public Task<StartLocalRaidResponseData> CompleteRaidStart(Task<StartLocalRaidResponseData> native)
        {
            taskOwnsLease = true;
            return CompleteRaidStartCore(native);
        }

        private async Task<StartLocalRaidResponseData> CompleteRaidStartCore(Task<StartLocalRaidResponseData> native)
        {
            try
            {
                var response = await native;
                (heldOwner ?? throw new ObjectDisposedException(nameof(AccountOperationLease))).RegisterStartCore(sessionId, request!, response);
                return response;
            }
            finally { Dispose(); }
        }

        public ValueTask<T> CompleteLogin<T>(ValueTask<T> native)
        {
            taskOwnsLease = true;
            return CompleteLoginCore(native);
        }

        private async ValueTask<T> CompleteLoginCore<T>(ValueTask<T> native)
        {
            try { return await native; }
            finally { Dispose(); }
        }

        // A foreign prefix can skip/throw before a Task exists. If a later
        // postfix throws, the already-running original Task retains ownership.
        public void AbortBeforeTask() { if (!taskOwnsLease) Dispose(); }
        public void Dispose() => Interlocked.Exchange(ref heldOwner, null)?.Gate(sessionId.ToString()).Release();
    }
}
