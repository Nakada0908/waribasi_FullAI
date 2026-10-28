using System.Collections.Generic;

namespace Waribasi.Presentation
{
    public static class PresentationVisibility
    {
        private static readonly HashSet<object> blockers = new HashSet<object>();
        public static bool Obscured => blockers.Count > 0;

        public static void Reset()
        {
            blockers.Clear();
        }

        public static void Set(object owner, bool obscured)
        {
            bool before = Obscured;
            if (obscured)
            {
                blockers.Add(owner);
            }
            else
            {
                blockers.Remove(owner);
            }
            if (before == Obscured)
            {
                return;
            }
            var flow = MatchFlowPresenter.Instance;
            var hud = MatchHudPresenter.Instance;
            if (flow != null)
            {
                flow.SetTransitionObscured(Obscured);
            }
            if (hud != null)
            {
                hud.SetTransitionObscured(Obscured);
            }
        }
    }
}
