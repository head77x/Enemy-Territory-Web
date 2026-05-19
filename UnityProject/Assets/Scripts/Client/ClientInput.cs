using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Client
{
    public static class ClientInput
    {
        // Button bit flags matching ET's button definitions
        private const int BUTTON_ATTACK         = 1;
        private const int BUTTON_TALK           = 2;
        private const int BUTTON_USE_HOLDABLE   = 4;
        private const int BUTTON_GESTURE        = 8;
        private const int BUTTON_WALKING        = 16;
        private const int BUTTON_SPRINT         = 32;
        private const int BUTTON_ACTIVATE       = 64;
        private const int BUTTON_ANY            = 128;

        // Input state — set by a MonoBehaviour input handler each frame
        public static float  mx;
        public static float  my;
        public static bool   attackDown;
        public static bool   useDown;
        public static bool   sprintDown;
        public static bool   walkDown;
        public static bool   activateDown;
        public static float  forwardAxis;
        public static float  rightAxis;
        public static float  upAxis;
        public static int    currentWeapon;

        public static int CL_CmdButtons()
        {
            int buttons = 0;

            if (attackDown)   buttons |= BUTTON_ATTACK;
            if (useDown)      buttons |= BUTTON_USE_HOLDABLE;
            if (sprintDown)   buttons |= BUTTON_SPRINT;
            if (walkDown)     buttons |= BUTTON_WALKING;
            if (activateDown) buttons |= BUTTON_ACTIVATE;

            // BUTTON_ANY is set when any button is pressed
            if (buttons != 0) buttons |= BUTTON_ANY;

            return buttons;
        }

        public static UserCmd CL_CreateCmd(int serverTime)
        {
            UserCmd cmd = new UserCmd();

            cmd.ServerTime  = serverTime;

            // Convert view angles to shorts
            cmd.Angles[0] = AngleToShort(ClientMain.Cl.ViewAngles0);
            cmd.Angles[1] = AngleToShort(ClientMain.Cl.ViewAngles1);
            cmd.Angles[2] = AngleToShort(ClientMain.Cl.ViewAngles2);

            // Movement axes clamped to sbyte range
            cmd.ForwardMove = ETMath.ClampChar((int)(forwardAxis * 127f));
            cmd.RightMove   = ETMath.ClampChar((int)(rightAxis   * 127f));
            cmd.UpMove      = ETMath.ClampChar((int)(upAxis      * 127f));

            cmd.Buttons = CL_CmdButtons();
            cmd.Weapon  = currentWeapon;

            return cmd;
        }

        public static void CL_FinishMove(UserCmd cmd)
        {
            ClientMain.Cl.CmdNumber++;
            int idx = ClientMain.Cl.CmdNumber & ClientConst.CMD_MASK;
            ClientMain.Cl.Cmds[idx] = cmd;

            // Accumulate mouse deltas into client state
            ClientMain.Cl.MouseDx += (int)mx;
            ClientMain.Cl.MouseDy += (int)my;

            mx = 0f;
            my = 0f;
        }

        private static short AngleToShort(float angle)
        {
            return (short)(angle * 65536f / 360f);
        }
    }
}
