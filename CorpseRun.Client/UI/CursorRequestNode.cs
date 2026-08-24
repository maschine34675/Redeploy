using System;
using EFT.GlobalEvents;
using EFT.InputSystem;
using UnityEngine;

namespace CorpseRun.UI
{
    /// <summary>
    /// Asks the game for the mouse cursor by voting in its input tree, which is
    /// the one mechanism here that several mods can use at the same time.
    ///
    /// The game decides once per frame whether the cursor should be shown, and
    /// writes the cursor state only when the live state disagrees - so a mod
    /// setting <c>Cursor.visible</c> is itself what makes it disagree, and the
    /// two alternate at frame rate. That was the flicker.
    ///
    /// The obvious alternative, the global <c>ToggleShowInGameCursorEvent</c>
    /// flag, fixes the flicker but is a single uncounted bool: whoever releases
    /// it takes the cursor from everyone else, and the release runs the game's
    /// full hide path - including <c>CursorLockMode.Locked</c>, which warps the
    /// pointer to the middle of the screen. A window being dragged at that
    /// moment jumps with it.
    ///
    /// The input tree has no such problem, because it has no release at all.
    /// <c>InputNode.TranslateInput</c> takes the MAXIMUM of every node's
    /// <c>ShouldLockCursor()</c>, recomputed from scratch on each dispatch, and
    /// <c>ShowCursor</c> outranks the <c>LockCursor</c> that the raid asks for.
    /// A node that stops wanting the cursor simply stops raising the maximum;
    /// there is nothing to take away from anybody. It is also how the game's
    /// own screens do it, and how Fika's chat window does it.
    /// </summary>
    internal sealed class CursorRequestNode : InputNode
    {
        private static CursorRequestNode _node;
        private static bool _unavailable;
        private bool _wanted;

        /// <summary>
        /// The whole surface: say whether this mod wants the cursor. Cheap
        /// enough to call on a state change, and there is no per-frame path -
        /// the vote is read by the game, not pushed by us.
        /// </summary>
        internal static void Want(bool on)
        {
            if (_unavailable)
            {
                RequestThroughGlobalFlag(on);
                return;
            }

            try
            {
                Vote(on);
            }
            catch (Exception error)
            {
                // A subclass of a game type fails hard rather than gracefully:
                // if the base class changes shape, resolving it throws. Catch it
                // HERE rather than inside Vote - the runtime resolves the base
                // type when it compiles the method that names it, so the guard
                // has to sit in a method that does not.
                _unavailable = true;
                Plugin.ModLogger.LogWarning("the input tree could not be used for the cursor ("
                    + error.GetType().Name + "); falling back to the global flag.");
                RequestThroughGlobalFlag(on);
            }
        }

        private static void Vote(bool on)
        {
            if (_node == null)
            {
                // Not at plugin start: the game builds this object well after
                // BepInEx loads. Looked up only when the answer changes, never
                // per frame - GameObject.Find walks the scene.
                GameObject input = GameObject.Find("___Input");
                InputTree tree = input == null ? null : input.GetComponent<InputTree>();
                if (tree == null)
                    return;

                var host = new GameObject("CorpseRun_CursorNode");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _node = host.AddComponent<CursorRequestNode>();
                tree.Add(_node);
            }

            _node._wanted = on;
        }

        /// <summary>
        /// The fallback for a game whose input tree this no longer fits: the
        /// global flag, which works but cannot share.
        /// </summary>
        private static void RequestThroughGlobalFlag(bool on)
        {
            try
            {
                GlobalEventsController.Instance
                    .CreateCommonEvent<ToggleShowInGameCursorEvent>()
                    .Invoke(on);
            }
            catch
            {
                // Nothing left to try; a missing cursor is not worth an
                // exception escaping into a UI update.
            }
        }

        public override ECursorResult ShouldLockCursor() =>
            _wanted ? ECursorResult.ShowCursor : ECursorResult.Ignore;

        public override ETranslateResult TranslateCommand(ECommand command) =>
            ETranslateResult.Ignore;

        public override void TranslateAxes(ref float[] axes)
        {
        }
    }
}
