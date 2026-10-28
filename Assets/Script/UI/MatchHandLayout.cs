using System;
using UnityEngine;
using Waribasi.Rules;

namespace Waribasi.Presentation
{
    public static class MatchHandLayout
    {
        public static void Place(Transform hand, PlayerId player, HandSide side)
        {
            if (player != PlayerId.PlayerA && player != PlayerId.PlayerB)
            {
                throw new ArgumentOutOfRangeException(nameof(player));
            }
            if (side != HandSide.Left && side != HandSide.Right)
            {
                throw new ArgumentOutOfRangeException(nameof(side));
            }
            int seat = (int)player;
            hand.SetPositionAndRotation(new Vector3(3 * Mathf.Abs((int)side - seat), 0, seat * 10),
                Quaternion.Euler(0, seat * 180, 0));
        }
    }
}
