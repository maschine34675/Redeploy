using System.Runtime.CompilerServices;
using BepInEx;
using EFT;
using UnityEngine;
namespace HandsAreNotBusy
{
#if FUTURE_MODS
    [BepInPlugin("com.lacyway.hanb", "Hands Are Not Busy", "1.8.0")]
#else
    [BepInPlugin("com.lacyway.hanb", "Hands Are Not Busy", "1.7.1")]
#endif
    public sealed class HANB_Plugin : BaseUnityPlugin { }
    public sealed class HANB_Component : MonoBehaviour
    {
        private LocalPlayer _player;
        public void SetPlayer(LocalPlayer player) => _player = player;
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private void FixHandsController(Player player) => player.HandResets++;
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public void Press() => FixHandsController(_player);
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public void ResetOther(Player player) => FixHandsController(player);
    }
}
