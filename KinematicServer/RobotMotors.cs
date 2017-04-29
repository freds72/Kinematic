using MonoBrickFirmware.Display;
using MonoBrickFirmware.Movement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace KinematicServer
{
    class RobotMotors : IDisposable
    {
        bool disposed = false;
        readonly EventWaitHandle _changedWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
        WaitHandle[] _motorTasks = new WaitHandle[3];
        static readonly ManualResetEvent _completedTask = new ManualResetEvent(true);
        readonly Motor[] _motors = new Motor[3];
        readonly List<IRobotCommand> _commands = new List<IRobotCommand>();

        Thread _thread;
        bool _run = true;

        public SByte MainMotorSpeed { get; set; }
        public SByte SecondaryMotorSpeed { get; set; }
        public SByte HandMotorSpeed { get; set; }
        public float MainMotorRatio { get; set; }
        public float SecondaryMotorRatio { get; set; }
        public float HandMotorRatio { get; set; }

        public RobotMotors(MotorPort mainMotor, MotorPort secondaryMotor, MotorPort handMotor)
        {
            _motors[0] = new Motor(mainMotor);
            _motors[1] = new Motor(secondaryMotor);
            _motors[2] = new Motor(handMotor);

            // default values
            MainMotorSpeed = SByte.MaxValue;
            SecondaryMotorSpeed = SByte.MaxValue;
            HandMotorSpeed = SByte.MaxValue;

            _thread = new Thread(MotorPollThread);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public void Queue(IRobotCommand command)
        {
            // make sure a single thread is changing values
            lock (_commands)
            {                
                _commands.Add(command);
            }
            // notify worker thread
            _changedWaitHandle.Set();
        }

        private List<IRobotCommand> DequeueAll()
        {
            // clone the current command stack
            lock (_commands)
            {
                var temp = new List<IRobotCommand>(_commands);
                _commands.Clear();
                return temp;
            }
        }

        public int GetRawTacho(MotorPort port)
        {
            return _motors[(int)port].GetTachoCount();
        }

        void Do(Action<Motor> action)
        {
            for (int i = 0; i < _motors.Length; i++)
                if (_motors[i] != null)
                    action(_motors[i]);
        }
        /// <summary>
        /// Turns off motor
        /// </summary>
        public void Off()
        {
            Do((m) => m.Off());
        }

        #region implements IDisposable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern.
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                _run = false;
                _changedWaitHandle.Set();
                _thread.Join();
                Do((m) => m.Off());
                for (int i = 0; i < _motors.Length; i++)
                    _motors[i] = null;
            }
            // Free any unmanaged objects here.
            disposed = true;
        }
        #endregion

        /// <summary>
        /// Reset tachymeter
        /// </summary>
        public void ResetTachos()
        {
            lock (_commands)
                _commands.Clear();
            Do((m) => m.ResetTacho());
        }

        void ResetTacho(int i)
        {
            lock (_commands)
                _commands.Clear();
            _motors[i].ResetTacho();
        }

        static int GetShortestPath(int err, int mod)
        {
            int altErr = err < 0 ? mod + err : err - mod;
            if (Math.Abs(altErr) < Math.Abs(err))
                return altErr;
            return err;
        }

        static WaitHandle Rotate(Motor motor, int targetTacho, float ratio, sbyte speed)
        {
            int mod = (int)(360 * ratio);

            // rebase
            // get actual position (raw) & rebase to [0,mod]
            int motorTacho = motor.GetTachoCount() % mod;
            if (motorTacho < 0)
                motorTacho += mod;

            // get target value
            targetTacho = (int)Math.Round(targetTacho * ratio, MidpointRounding.AwayFromZero) % mod;
            if (targetTacho < 0)
                targetTacho += mod;
            int err = (targetTacho - motorTacho);
            if (err != 0)
            {
                err = GetShortestPath(err, mod);
                return motor.SpeedProfile((err > 0) ? speed : (sbyte)-speed, 0, (uint)Math.Abs(err), 0, true);
            }

            return _completedTask;
        }

        public enum CalibrationSteps
        {
            Main,
            Secondary,
            SecondaryReset, // find middle spot
            Test,
            Pause
        }
        public void Calibrate(Func<CalibrationSteps, bool> calibrated, bool skip = false)
        {
            Motor motor = null;

            ResetTachos();

            // calibrate hand
            motor = _motors[2];
            int handTacho = int.MaxValue;
            motor.SpeedProfile(-32, 0,180, 0, false);
            // rotate until blocked
            while ((handTacho != motor.GetTachoCount()))
            {
                handTacho = motor.GetTachoCount();
                Thread.Sleep(75);
            }
            motor.Brake();
            Off();

            // pen up
            motor.SpeedProfile(127, 0, 180, 0, true).WaitOne();

            // found limit
            ResetTachos();

            if (skip)
                return;

            motor = _motors[0];
            motor.SpeedProfile(-64, 0, (uint)Math.Abs(180 * MainMotorRatio), 0, false);
            while (!calibrated(CalibrationSteps.Main)) ;
            Off();

            // found limit
            ResetTachos();

            // move to neutral position
            motor.SpeedProfile(64, 0, (uint)Math.Abs(90 * MainMotorRatio), 0, true).WaitOne();

            // seconday 
            for (int i = 0; i < 3; i++)
            {
                motor = _motors[1];
                motor.SpeedProfile(-64, 0, (uint)Math.Abs(360 * SecondaryMotorRatio), 0, false);
                while (!calibrated(CalibrationSteps.Secondary)) ;
                motor.Brake();
                motor.Off();
                int right = motor.GetTachoCount();

                // while (!calibrated(CalibrationSteps.Pause)) ;

                // reset max
                calibrated(CalibrationSteps.SecondaryReset);

                // secondary (2nd step)
                motor.SpeedProfile(64, 0, (uint)Math.Abs(360 * SecondaryMotorRatio), 0, false);
                while (!calibrated(CalibrationSteps.Secondary)) ;
                motor.Brake();
                motor.Off();
                int left = motor.GetTachoCount();

                //while (!calibrated(CalibrationSteps.Pause)) ;

                // find middle point
                int err = (right - left) / 2;
                if (err != 0)
                    motor.SpeedProfile((err > 0) ? (sbyte)64 : (sbyte)-64, 0, (uint)Math.Abs(err), 0, true).WaitOne();
                else
                    break; // nothing to do - we are good!

                //while (!calibrated(CalibrationSteps.Pause)) ;
            }

            // found limit for second motor
            ResetTacho(1);
        }

        void Do(HandCommand command)
        {
            // ------------------------------------
            // main motor rotation
            _motorTasks[0] = _completedTask;

            // -----------------------------------------------------
            // secondary motor rotation 
            _motorTasks[1] = _completedTask;

            // Hand (not used for now)
            _motorTasks[2] = _motors[2].SpeedProfile((sbyte)(command.Up?127:-127), 0, 180, 0, true);
        }

        void Do(MoveCommand command)
        {
            // ------------------------------------
            // main motor rotation
            _motorTasks[0] = Rotate(_motors[0], command.MainRotation, MainMotorRatio, MainMotorSpeed);

            // -----------------------------------------------------
            // secondary motor rotation 
            _motorTasks[1] = Rotate(_motors[1], command.SecondaryRotation, SecondaryMotorRatio, SecondaryMotorSpeed);

            // Hand (not used for now)
            _motorTasks[2] = _completedTask;
        }

        void MotorPollThread()
        {
            while (_run)
            {
                // wait until there is something to do
                _changedWaitHandle.WaitOne();

                // take all pending comamnds
                List<IRobotCommand> commands = DequeueAll();
                for(int i=0;i<commands.Count && _run;i++)
                {
                    IRobotCommand command = commands[i];

                    if (command.GetType() == typeof(HandCommand))
                    { 
                        Do((HandCommand)command);
                    }
                    else if ( command.GetType() == typeof(MoveCommand))
                    {
                        Do((MoveCommand)command);
                    }
                    else
                    {
                        // unknown command
                    }

                    // wait for all motors to finish
                    WaitHandle.WaitAll(_motorTasks);
                }
            }
        }
    }
}
