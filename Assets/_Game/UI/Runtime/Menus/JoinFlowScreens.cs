using KryticalNova.Networking;
using UnityEngine;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The menus' side of joining a server (<see cref="JoinFlow"/>): the loading screen holds while the game joins, and a
    /// join that failed goes back to character select, which opens the Servers window with the reason.
    /// </summary>
    public static class JoinFlowScreens
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Listen()
        {
            JoinFlow.Started += name => LoadingGate.Require("Joining " + name, () => !JoinFlow.Joining);
            JoinFlow.Failed += _ => FrontEnd.Go(FrontEnd.CharacterSelectScene);
        }
    }
}
