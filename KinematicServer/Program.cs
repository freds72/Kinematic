using MonoBrickFirmware.Display;
using MonoBrickFirmware.Movement;
using MonoBrickFirmware.UserInput;
using MonoBrickFirmware.Sensors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace KinematicServer
{
    class Program
    {
        static void Log(string log)
        {
            // if (MonoBrickFirmware.Tools.PlatFormHelper.RunningPlatform == MonoBrickFirmware.Tools.PlatFormHelper.Platform.EV3)
            {
                LcdConsole.WriteLine("{0}", log);
            }
            //else
            //{
            //    Console.WriteLine(log);
            //}
        }

        static void Main(string[] args)
        {
            ButtonEvents buttons = new ButtonEvents();
            TcpListener server = null;
            TcpClient client = null;
            bool run = true;
            buttons.EscapePressed += () =>
            {
                if (server != null)
                    server.Stop();
                if (client != null)
                    client.Close();

                run = false;
            };

            Log("KinematicServer 1.5");
            
            float mainRatio = 46.667f;
            float secondaryRatio = 40.0f;
            float handRatio = 8f;
#if DUMPLOGS
            StringBuilder logs = new StringBuilder();
#endif
            using (RobotMotors motors = new RobotMotors(MotorPort.OutA, MotorPort.OutB, MotorPort.OutC) { MainMotorRatio = mainRatio, SecondaryMotorRatio = secondaryRatio, HandMotorRatio = handRatio })
            {
                EV3TouchSensor sensor1 = new EV3TouchSensor(SensorPort.In1);
                EV3ColorSensor sensor2 = new EV3ColorSensor(SensorPort.In2);
                sensor2.Mode = ColorMode.Reflection;

                int sensor2max = -1;
                int pause = 0;
                buttons.EnterReleased += () =>
                {
                    pause++;
                };
                int currentPause = 0;
                motors.Calibrate((RobotMotors.CalibrationSteps step) =>
                {
                    if (!run) return true;
                    switch (step)
                    {
                        case RobotMotors.CalibrationSteps.Main:
                            if (sensor1.IsPressed() )
                                return true;
                            break;

                        case RobotMotors.CalibrationSteps.Pause:                            
                            if ( currentPause != pause)
                            {
                                currentPause = pause;
                                return true;
                            }
                            break;

                        case RobotMotors.CalibrationSteps.Secondary:
                            int sensor2reading = sensor2.ReadRaw();
                            if (sensor2reading > 19 &&
                                sensor2reading < sensor2max) // moving away from sweet spot (but remove noise)                                
                            {
#if DUMPLOGS
                                logs.Append(string.Format("{0}:{1}:{2}\n\n", motors.GetRawTacho(MotorPort.OutB), sensor2reading, sensor2max));
                                logs.Append("-----------------\n\n");
#endif
                                return true;
                            }
                            else if (sensor2reading > sensor2max)
                                sensor2max = sensor2reading;  // keep going when it increases

#if DUMPLOGS
                            logs.Append(string.Format("{0}:{1}:{2}\n\n", motors.GetRawTacho(MotorPort.OutB), sensor2reading, sensor2max));                            
#endif
                        break;

                        case RobotMotors.CalibrationSteps.SecondaryReset:
                            sensor2max = -1;
#if DUMPLOGS
                            logs.Append("***********************\n\n");
#endif
                            break;
                    }
                    /*
                    Lcd.Clear();
                    int line = 0;
                    Lcd.WriteText(Font.MediumFont, new Point(0, line), "Calibrating...", true);
                    line += (int)(Font.MediumFont.maxHeight);
                    Lcd.WriteText(Font.MediumFont, new Point(0, line), string.Format("Refl.: {0} / {1}", sensor2.ReadRaw(), sensor2max), true);
                    line += (int)(Font.MediumFont.maxHeight);
                    Lcd.WriteText(Font.MediumFont, new Point(0, line), string.Format("A: {0}", motors.GetRawTacho(MotorPort.OutA)), true);
                    line += (int)(Font.MediumFont.maxHeight);
                    Lcd.WriteText(Font.MediumFont, new Point(0, line), string.Format("B: {0}", motors.GetRawTacho(MotorPort.OutB)), true);
                    line += (int)(Font.MediumFont.maxHeight);
                    Lcd.Update();
                    */

                    return false;
                });
            }
            // stop here
            if (!run)
                return;

            // main loop
            Lcd.Clear();
            Lcd.Update();
            Log("Starting...");
            try
            {
                using (RobotMotors motors = new RobotMotors(MotorPort.OutA, MotorPort.OutB, MotorPort.OutC) { MainMotorRatio = mainRatio, SecondaryMotorRatio = secondaryRatio, HandMotorRatio = handRatio })
                {
                    // Set the TcpListener on port 13000.
                    Int32 port = 13000;

                    // TcpListener server = new TcpListener(port);
                    server = new TcpListener(IPAddress.Any, port);

                    // Start listening for client requests.
                    server.Start();

                    // Buffer for reading data
                    Byte[] bytes = new Byte[256];
                    String data = null;

                    // Enter the listening loop.
                    while (run)
                    {
                        Log("Waiting for a connection... ");

                        // Perform a blocking call to accept requests.
                        // You could also user server.AcceptSocket() here.
                        client = server.AcceptTcpClient();
                        Log("Connected!");

#if DUMPLOGS
                        // DEBUG
                        byte[] logBuffer = Encoding.ASCII.GetBytes(logs.ToString());
                        client.GetStream().Write(logBuffer, 0, logBuffer.Length);
#endif
                        motors.MainMotorSpeed = 64;
                        motors.SecondaryMotorSpeed = 64;
                        motors.HandMotorSpeed = 127;
                        data = null;

                        // Get a stream object for reading and writing
                        NetworkStream stream = client.GetStream();

                        int read;
                        string message = "";
                        // Loop to receive all the data sent by the client.
                        int commandCount = 0;
                        while (run && (read = stream.Read(bytes, 0, bytes.Length)) != 0)
                        {
                            // Translate data bytes to a ASCII string.
                            data = System.Text.Encoding.ASCII.GetString(bytes, 0, read);
                            for (int i = 0; i < read; i++)
                            {
                                char c = data[i];
                                if (c != '\0')
                                    message += c;
                                else
                                {
                                    String[] rawCommand = message.Split(';');                                    
                                    // get message type
                                    switch (rawCommand[0])
                                    {
                                        case "UP":
                                            motors.Queue(new HandCommand(true));
                                            commandCount = 0;
                                            break;
                                        case "DWN":
                                            motors.Queue(new HandCommand(false));
                                            commandCount = 0;
                                            break;
                                        case "MOV":
                                            commandCount++;
                                            float mainRotation = float.Parse(rawCommand[1]);
                                            float secondaryRotation = float.Parse(rawCommand[2]);

                                            motors.Queue(
                                                new MoveCommand {
                                                       MainRotation = (int)Math.Round(mainRotation, MidpointRounding.AwayFromZero),
                                                       SecondaryRotation = (int)Math.Round(secondaryRotation, MidpointRounding.AwayFromZero)
                                                });
                                            break;
                                    }

                                    //
                                    Lcd.Clear();
                                    int line = 0;
                                    Lcd.WriteText(Font.MediumFont, new Point(0, line), string.Format("Count: {0}", commandCount), true);
                                    line += (int)(Font.MediumFont.maxHeight);
                                    Lcd.WriteText(Font.MediumFont, new Point(0, line), string.Format("Last: {0}", message), true);
                                    line += (int)(Font.MediumFont.maxHeight);
                                    Lcd.Update();

                                    // 
                                    message = "";
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                if (client.Connected)
                {
                    byte[] buffer = Encoding.ASCII.GetBytes(ex.Message);
                    client.GetStream().Write(buffer, 0, buffer.Length);
                }
                throw;
            }
            finally
            {
                // Stop listening for new clients.
                server.Stop();
            }
        }
    }
}
