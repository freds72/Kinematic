using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Kinematic
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        // The texture this particle system will use.
        CanvasBitmap _bitmap;
        Vector2 _bitmapCenter;
        Rect _bitmapBounds;

        const int simulationW = 512;
        const int simulationH = 512;

        Transform2DEffect transformEffect;
        CanvasRenderTarget currentSurface, nextSurface;
        static TimeSpan normalTargetElapsedTime = TimeSpan.FromSeconds(1.0 / 30.0);
        static TimeSpan slowTargetElapsedTime = TimeSpan.FromSeconds(0.25);

        List<List<Vector2>> _jobs = new List<List<Vector2>>();
        public event PropertyChangedEventHandler PropertyChanged;

        Socket _socket;

        public MainPage()
        {
            this.InitializeComponent();
            ServerAddress = "10.0.1.1:13000";
            this.DataContext = this;
            canvas.TargetElapsedTime = normalTargetElapsedTime;
        }

        void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            this.canvas.RemoveFromVisualTree();
            this.canvas = null;
        }

        void RaiseChange(string name)
        {
            if (Dispatcher.HasThreadAccess)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
            else
            {
                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                });
            }
        }

        string _serverAddress;
        public string ServerAddress
        {
            get { return _serverAddress; }
            set
            {
                _serverAddress = value;
                RaiseChange("ServerAddress");
            }
        }

        string _rawCommand;
        public string RawCommand
        {
            get { return _rawCommand; }
            set
            {
                _rawCommand = value;
                RaiseChange("RawCommand");
            }
        }

        string _log;
        public string Log
        {
            get { return _log; }
            set
            {
                _log = value;
                RaiseChange("Log");
            }
        }

        string _text2draw;
        public string Text2Draw
        {
            get { return _text2draw; }
            set
            {
                _text2draw = value;
                RaiseChange("Text2Draw");
            }
        }

        public bool Slow
        {
            get
            {
                return canvas.TargetElapsedTime != normalTargetElapsedTime;
            }
            set
            {
                if (value)
                    canvas.TargetElapsedTime = slowTargetElapsedTime;
                else
                    canvas.TargetElapsedTime = normalTargetElapsedTime;
            }
        }

        static Matrix3x2 GetDisplayTransform(ICanvasAnimatedControl canvas)
        {
            var outputSize = canvas.Size.ToVector2();
            var sourceSize = new Vector2(canvas.ConvertPixelsToDips(simulationW), canvas.ConvertPixelsToDips(simulationH));

            return Utils.GetDisplayTransform(outputSize, sourceSize);
        }

        private void canvas_Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
        {
            // Swap the current and next surfaces.
            var tmp = currentSurface;
            currentSurface = nextSurface;
            nextSurface = tmp;
        }

        private void canvas_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
        {
            Draw();

            // Display the current surface.
            transformEffect.Source = currentSurface;
            transformEffect.TransformMatrix = GetDisplayTransform(sender);
            args.DrawingSession.DrawImage(transformEffect);
            
        }

        static float l1 = 136; // 17 studs
        static float l2 = 80; // 10 studs
        DateTime _wakeUpTime;
        int _jobPosIndex = 0;
        Vector2 _targetPosition = Vector2.Zero;
        private void Draw()
        {
            using (var ds = currentSurface.CreateDrawingSession())
            {
                ds.Clear(Colors.Black);
                ds.Transform =
                    Matrix3x2.Multiply(
                        Matrix3x2.CreateScale(new Vector2(1, -1)),
                        Matrix3x2.CreateTranslation(new Vector2(simulationW / 2, simulationH / 2)));

                if (_tempMousePositions != null)
                {
                    for (int i = 1; i < _tempMousePositions.Count; i++)
                    {
                        var p1 = new Vector2(_tempMousePositions[i - 1].X - simulationW / 2, simulationH / 2 - _tempMousePositions[i - 1].Y);
                        var p2 = new Vector2(_tempMousePositions[i].X - simulationW / 2, simulationH / 2 - _tempMousePositions[i].Y);
                        ds.DrawLine(p1, p2, Colors.Yellow);
                    }
                }

                bool syncRobot = false;
                bool beginFigure = false;
                bool endFigure = false;
                lock (_jobs)
                {
                    if (_jobs.Count > 0 && DateTime.Now >= _wakeUpTime)
                    {
                        // get oldest list
                        List<Vector2> positions = _jobs[0];
                        if (positions.Count > 0)
                        {
                            if (_jobPosIndex < positions.Count)
                            {
                                if (_jobPosIndex == 0)
                                    beginFigure = true;
                                _targetPosition = positions[_jobPosIndex++];                               
                            }
                            else
                            {
                                endFigure = true;
                                _jobs.RemoveAt(0);
                                _jobPosIndex = 0;
                            }

                            if (_socket != null && _socket.Connected)
                                syncRobot = true;
                        }
                        // still something to do?
                        if (_jobs.Count > 0)
                            _wakeUpTime = DateTime.Now + TimeSpan.FromMilliseconds(125);
                        else
                            System.Diagnostics.Debug.WriteLine("no jobs pending");

                        // draw points
                        foreach (List<Vector2> remainingPaths in _jobs)
                        {
                            for (int i = 1; i < remainingPaths.Count; i++)
                            {
                                var p1 = new Vector2(remainingPaths[i - 1].X - simulationW / 2, simulationH / 2 - remainingPaths[i - 1].Y);
                                var p2 = new Vector2(remainingPaths[i].X - simulationW / 2, simulationH / 2 - remainingPaths[i].Y);
                                ds.DrawLine(p1, p2, Colors.LightSteelBlue);
                            }
                        }
                    }
                }

                var mx = _mousePosition.X - simulationW / 2;
                var my = simulationH / 2 - _mousePosition.Y;
                ds.DrawImage(_bitmap, new Vector2(mx, my));

                // stick to last known position
                var x = _targetPosition.X - simulationW / 2;
                var y = simulationH / 2 - _targetPosition.Y;

                Vector2 pos = new Vector2(x, y);
                Vector2 normalizedPos = Vector2.Normalize(pos);
                if (pos.LengthSquared() > (l1 + l2) * (l1 + l2))
                {
                    Vector2 clamped = Vector2.Multiply((float)(l1 + l2), normalizedPos);
                    x = clamped.X;
                    y = clamped.Y;
                }

                // http://thingsiamdoing.com/inverse-kinematics/
                double L = Math.Sqrt(x * x + y * y);
                double a = Math.Acos((l1 * l1 + L * L - l2 * l2) / (2 * l1 * L));
                double b = Math.Acos((l1 * l1 + l2 * l2 - L * L) / (2 * l1 * l2));
                double XL = Math.Atan2(normalizedPos.Y, normalizedPos.X) - Math.Atan2(0, 1);
                if (XL < 0)
                    XL += 2 * Math.PI;
                double o1 = XL - a;
                double o2 = Math.PI - b + o1;

                ds.DrawCircle(Vector2.Zero, l1 + l2, Colors.Gray, 2);
                // robot arm
                ds.DrawLine(Vector2.Zero, new Vector2((float)(l1 * Math.Cos(o1)), (float)(l1 * Math.Sin(o1))), Colors.Green, 10);
                ds.DrawLine(
                    new Vector2((float)(l1 * Math.Cos(o1)), (float)(l1 * Math.Sin(o1))),
                    new Vector2((float)(l1 * Math.Cos(o1) + l2 * Math.Cos(o2)), (float)(l1 * Math.Sin(o1) + l2 * Math.Sin(o2))), Colors.Red, 10);

                ds.Transform = Matrix3x2.CreateTranslation(new Vector2(simulationW / 2, simulationH / 2));
                ds.DrawText(string.Format("{0:0.00}:{1:0.00}", 180 * o1 / Math.PI, 180 * (b) / Math.PI), new Vector2(x, -y), Colors.Green);

                if (syncRobot)
                {
                    // send that command to the robot
                    SocketAsyncEventArgs completeArgs = new SocketAsyncEventArgs();
                    string robotCommand = "";
                    if (beginFigure)
                        robotCommand = "MOV;{0:0.00};{1:0.00}\0DWN;\0";
                    else if (endFigure)
                        robotCommand = "UP;\0";
                    else
                        robotCommand = "MOV;{0:0.00};{1:0.00}\0";

                    byte[] buffer = Encoding.ASCII.GetBytes(string.Format(robotCommand, 180 * o1 / Math.PI, (180 * (b) / Math.PI)));
                    completeArgs.SetBuffer(buffer, 0, buffer.Length);
                    completeArgs.UserToken = _socket;
                    completeArgs.RemoteEndPoint = _socket.RemoteEndPoint;
                    _socket.SendAsync(completeArgs);
                }
            }
        }

        private void canvas_CreateResources(CanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
        {
            if (args.Reason == CanvasCreateResourcesReason.DpiChanged)
                return;

            const float defaultDpi = 96;

            currentSurface = new CanvasRenderTarget(sender, simulationW, simulationH, defaultDpi);
            nextSurface = new CanvasRenderTarget(sender, simulationW, simulationH, defaultDpi);

            transformEffect = new Transform2DEffect
            {
                Source = currentSurface,
                InterpolationMode = CanvasImageInterpolation.NearestNeighbor,
            };

            args.TrackAsyncAction(CreateResourcesAsync(sender).AsAsyncAction());
        }

        async Task CreateResourcesAsync(CanvasAnimatedControl sender)
        {
            await CreateBitmapResourcesAsync(sender);
        }

        async Task CreateBitmapResourcesAsync(CanvasAnimatedControl sender)
        {
            _bitmap = await CanvasBitmap.LoadAsync(sender, "Assets/blank.png");

            _bitmapCenter = _bitmap.Size.ToVector2() / 2;
            _bitmapBounds = _bitmap.Bounds;
        }

        Vector2 _mousePosition = Vector2.Zero;
        Vector2 _startingPoint = Vector2.Zero;
        List<Vector2> _tempMousePositions = null;

        private void canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _tempMousePositions = new List<Vector2>();
        }

        private void canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            lock (_jobs)
            {
                _jobs.Add(_tempMousePositions);
            }
            System.Diagnostics.Debug.WriteLine("Path length: {0}", _tempMousePositions.Count);

            _tempMousePositions = null;
            _wakeUpTime = DateTime.Now;
        }

        private void GenText_Click(object sender, RoutedEventArgs e)
        {
            // nothing to do?
            if (string.IsNullOrEmpty(_text2draw))
                return;

            var format = new CanvasTextFormat();
            format.FontSize = 48;           
           
            var textLayout = new CanvasTextLayout(canvas, _text2draw, format, simulationW, simulationH);
            using (CanvasGeometry geometry = CanvasGeometry.CreateText(textLayout))
            {
                CanvasStrokeStyle dashedStroke = new CanvasStrokeStyle()
                {
                    DashStyle = CanvasDashStyle.Dash                    
                };

                PathToGlyphBuilder streamer = new PathToGlyphBuilder();
                geometry
                    .Simplify(CanvasGeometrySimplification.CubicsAndLines)
                    .SendPathTo(streamer);
                Vector2 offset = new Vector2(simulationW / 2, 64);
                const int precision = 10;
                foreach(TextGlyph geometries in streamer.TextGlyphs)
                {
                    List<Vector2> points = new List<Vector2>();
                    foreach(IGlyphPath glyph in geometries)
                    {
                        for (int i = 0; i < precision+1; i++)
                            points.Add(glyph.Lerp(i / (float)precision)+ offset);
                    }
                    _jobs.Add(points);
                }
                //
                _wakeUpTime = DateTime.Now;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs re)
        {
            string result = string.Empty;

            // Create DnsEndPoint. The hostName and port are passed in to this method.
            string[] address = ServerAddress.Split(':');
            int port = 13000;
            DnsEndPoint hostEntry = new DnsEndPoint(address[0], address.Length==2?int.Parse(address[1] ,System.Globalization.NumberFormatInfo.InvariantInfo):port);

            // cleanup any previous connection
            if (_socket != null && _socket.Connected)
                _socket.Dispose();

            // Create a stream-based, TCP socket using the InterNetwork Address Family. 
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Create a SocketAsyncEventArgs object to be used in the connection request
            SocketAsyncEventArgs socketEventArg = new SocketAsyncEventArgs();
            socketEventArg.RemoteEndPoint = hostEntry;


            // Inline event handler for the Completed event.
            // Note: This event handler was implemented inline in order to make this method self-contained.
            socketEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate (object s, SocketAsyncEventArgs e)
            {
                Log = "Server response: " + e.SocketError.ToString();

                // listen 
                SocketAsyncEventArgs traceEventArg = new SocketAsyncEventArgs();
                traceEventArg.RemoteEndPoint = hostEntry;
                traceEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate (object ss, SocketAsyncEventArgs ee)
                {
                    byte[] buffer = ee.Buffer;
                    System.Diagnostics.Debug.WriteLine(System.Text.Encoding.ASCII.GetString(buffer, 0, buffer.Length));
                });                
                traceEventArg.SetBuffer(new byte[1024], 0, 1024);
                _socket.ReceiveAsync(traceEventArg);
            });

            Log = string.Format("Connecting {0}:{1}", hostEntry.Host, hostEntry.Port);

            // Make an asynchronous Connect request over the socket
            _socket.ConnectAsync(socketEventArg);
        }

        private void ButtonSend_Click(object sender, RoutedEventArgs e)
        {
            if (_socket != null && _socket.Connected)
            {
                // send that command to the robot
                SocketAsyncEventArgs completeArgs = new SocketAsyncEventArgs();
                byte[] buffer = Encoding.ASCII.GetBytes(string.Format("{0}\0", RawCommand));
                completeArgs.SetBuffer(buffer, 0, buffer.Length);
                completeArgs.UserToken = _socket;
                completeArgs.RemoteEndPoint = _socket.RemoteEndPoint;
                _socket.SendAsync(completeArgs);
            }
        }

        private void canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            Matrix3x2 transform;
            Matrix3x2.Invert(GetDisplayTransform(canvas), out transform);

            var pos = Vector2.Transform(e.GetCurrentPoint(canvas).Position.ToVector2(), transform);

            _mousePosition.X = canvas.ConvertDipsToPixels(pos.X, CanvasDpiRounding.Floor);
            _mousePosition.Y = canvas.ConvertDipsToPixels(pos.Y, CanvasDpiRounding.Floor);

            if (_tempMousePositions != null)
            {
                // register only points within the circle
                var x = _mousePosition.X - simulationW / 2;
                var y = simulationH / 2 - _mousePosition.Y;
                if ((x * x + y * y) < (l1 + l2) * (l1 + l2))
                {
                    Vector2 previousPos = new Vector2(65000, 65000); // arbitrary large value
                    if (_tempMousePositions.Count > 0)
                        previousPos = _tempMousePositions[_tempMousePositions.Count - 1];
                    if ( Vector2.DistanceSquared(previousPos, _mousePosition) > 32)
                        _tempMousePositions.Add(_mousePosition);
                }
            }
            //
            canvas.Invalidate();
        }
    }
}
