using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KinematicServer
{
    interface IRobotCommand
    {
    }

    struct HandCommand : IRobotCommand
    {
        public bool Up;
        public HandCommand(bool up)
        { Up = up; }
    }

    struct MoveCommand : IRobotCommand
    {
        public int MainRotation;
        public int SecondaryRotation;
    }
}
