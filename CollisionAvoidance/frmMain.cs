using Emgu.CV;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Emgu;
using Emgu.CV.Structure;
using System.IO.Pipes;
using System.IO;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CollisionAvoidance
{
    public partial class frmMain : Form
    {
        VideoCapture cap;
        Mat inputFrame = new Mat();
        CancellationTokenSource cancelEvent;
        Thread thrPipe;
        Thread thrPipeConnection;
        NamedPipeServerStream server;
        bool bHasPythonStarted;

        delegate void CollEventDelegate(CollisionEventArgs _args);
        event CollEventDelegate CollisionWarningEvent;
        EventHandler AlertEvent;

        internal class Detection
        {
            internal float Score;
            internal int Class;
            internal RectangleF BBox;
            internal double Azimuth;

            public Point GetCenter
            {
                get
                {
                    return BBox.Center();
                }

            }

            public Detection(float v, int c, RectangleF rectangleF, double az)
            {
                this.Score = v;
                this.Class = c;
                this.BBox = rectangleF;
                this.Azimuth = az;
            }
        }

        internal class CollisionEventArgs : EventArgs
        {
            internal Detection colDetect;
            public CollisionEventArgs()
            {

            }
            public CollisionEventArgs(Detection det)
            {
                this.colDetect = det;
            }
        }

        public frmMain()
        {
            InitializeComponent();
            this.Text = Program.version;
            cancelEvent = new CancellationTokenSource();
            CollisionWarningEvent += CollisionHandler;
            AlertEvent += OnReceivedMessage;
            bHasPythonStarted = false;
            SettingsHolder.PythonProcessId = -1;
        }

        private void imageGrabbedEvent(object sender, EventArgs e)
        {

            try
            {
                cap.Retrieve(inputFrame);

                Bitmap LatestAcquiredImage = inputFrame.ToBitmap();

                pictureBox1.Image = LatestAcquiredImage;
                //imgEntrada = m.ToImage<Bgr, byte>();
            }
            catch (Exception Ex)
            {

            }
        }
        private void CollisionHandler(CollisionEventArgs e)
        {
            AlertEvent.Raise(string.Format("Collision detected. AZ: {0:F3}", e.colDetect.Azimuth));
            SendUdpMessage(e.colDetect);
        }

        private void SendUdpMessage(Detection colDetect)
        {
            UdpClient udpc = new UdpClient();
            IPEndPoint remoteIPE = new IPEndPoint(IPAddress.Parse(SettingsHolder.Instance.IPAddress), SettingsHolder.Instance.IPPort);
            string msg = string.Format("#WARNING#TARGET#RNG#AZ#{0:F3}#{1:F3}", SettingsHolder.Instance.VDistance, colDetect.Azimuth);
            
            udpc.Send(msg.GetBytes(), msg.Length, remoteIPE);
            AlertEvent.Raise("Warning Sent");
        }

        private void OnReceivedMessage(object sender, EventArgs e)
        {
            string msg = sender as string;
            if (msg != null)
            {
                string sMsgToDisplay = DateTime.UtcNow.ToString("HH:mm:ss.fff") + ": " + msg;
                listBox1.InvokeIfRequired(
                    () =>
                    {
                        listBox1.Items.Insert(0, sMsgToDisplay);
                        if (listBox1.Items.Count > 150)
                        {
                            listBox1.Items.TrimTo(100);
                        }

                    });
                //lstMessages.Insert(0, sMsgToDisplay);
            }
        }
        void run_server()
        {

            if(!bHasPythonStarted)
            {
                Thread thrPyhtonProcess = new Thread(() => { CLI.RunProcess(ref bHasPythonStarted); });
                thrPyhtonProcess.IsBackground = true;
                thrPyhtonProcess.Start();
                
                
            }
            BinaryReader br = InitPipe();

            try
            {
                while (!cancelEvent.IsCancellationRequested)
                {
                    var len1 = (int)br.ReadUInt32();            // Read string length
                    var len2 = (int)br.ReadUInt32();
                    var len3 = (int)br.ReadUInt32();
                    var len4 = (int)br.ReadUInt32();
                    byte[] imgBytes = br.ReadBytes(len1 * len2 * len3);
                    byte[] scoresBytes = br.ReadBytes(len4 * sizeof(float));
                    byte[] classBytes = br.ReadBytes(len4);
                    byte[] Out_yminBytes = br.ReadBytes(len4 * sizeof(float));
                    byte[] Out_xminBytes = br.ReadBytes(len4 * sizeof(float));
                    byte[] Out_ymaxBytes = br.ReadBytes(len4 * sizeof(float));
                    byte[] Out_xmaxBytes = br.ReadBytes(len4 * sizeof(float));

                    byte[,,] imgBytes3d = new byte[len1, len2, len3];
                    Buffer.BlockCopy(imgBytes, 0, imgBytes3d, 0, imgBytes.Length);
                    Image<Bgr, byte> img = new Image<Bgr, byte>(imgBytes3d);

                    Console.WriteLine("Read: \"{0}\" bytes", imgBytes.Length);

                    int[] Classes = new int[len4];
                    float[] Scores = new float[len4];
                    float[] ymin = new float[len4];
                    float[] xmin = new float[len4];
                    float[] ymax = new float[len4];
                    float[] xmax = new float[len4];

                    Buffer.BlockCopy(imgBytes, 0, imgBytes3d, 0, imgBytes.Length);
                    Buffer.BlockCopy(classBytes, 0, Classes, 0, classBytes.Length);
                    Buffer.BlockCopy(scoresBytes, 0, Scores, 0, scoresBytes.Length);
                    Buffer.BlockCopy(Out_yminBytes, 0, ymin, 0, scoresBytes.Length);
                    Buffer.BlockCopy(Out_xminBytes, 0, xmin, 0, scoresBytes.Length);
                    Buffer.BlockCopy(Out_ymaxBytes, 0, ymax, 0, scoresBytes.Length);
                    Buffer.BlockCopy(Out_xmaxBytes, 0, xmax, 0, scoresBytes.Length);


                    List<Detection> lstDetect = new List<Detection>();
                    FillList(lstDetect, Scores, Classes, ymin, xmin, ymax, xmax, img.Size);
                    AlertEvent.Raise(string.Format("Received {0:D} targets above thresh ({1:F1})", lstDetect.Count, SettingsHolder.Instance.ScoreThresh));
                    if (SettingsHolder.Instance.ShowBoxes)
                    {
                        DrawDetectionBoxes(ref img, lstDetect);
                    }


                    CheckIfInDangerZone(ref img, lstDetect);

                    pictureBox1.Image = img.ToBitmap();

                    //var buf = Encoding.ASCII.GetBytes("received");     // Get ASCII byte array     
                    //bw.Write((uint)buf.Length);                // Write string length
                    //bw.Write(buf);                              // Write string
                    //Console.WriteLine("Wrote: \"{0}\" bytes", buf.Length);
                }
            }
            catch (EndOfStreamException)
            {
                server.Close();
                server.Dispose();
                cancelEvent = new CancellationTokenSource();
                // When client disconnects
            }
            catch (ThreadAbortException)
            {
                server.Close();
                server.Dispose();
                cancelEvent = new CancellationTokenSource();
            }
            finally
            {
                server.Close();
                server.Dispose();
                cancelEvent = new CancellationTokenSource();
            }


            Console.WriteLine("Client disconnected.");
            server.Close();
            server.Dispose();
            cancelEvent = new CancellationTokenSource();
        }

        private BinaryReader InitPipe()
        {
            // Open the named pipe.
            if (server == null)
            {
                server = new NamedPipeServerStream("DetectionData", PipeDirection.InOut, 2);
                AlertEvent.Raise(string.Format("Started module connection"));
            }
            else
            {
                server.Close();

            }
            thrPipeConnection = new Thread(() => { server.WaitForConnection(); });
            thrPipeConnection.Start();
            while ((thrPipeConnection.ThreadState & System.Threading.ThreadState.Stopped) == 0 || cancelEvent.IsCancellationRequested)
            {

            }


            var br = new BinaryReader(server);
            var bw = new BinaryWriter(server);
            return br;
        }

        private void CheckIfInDangerZone(ref Image<Bgr, byte> img, List<Detection> lstDetect)
        {
            Rectangle DZrect = Rectangle.Empty;
            double Left = img.Size.Width / 2 - (SettingsHolder.Instance.DZoneHor / 100 * img.Size.Width);
            double Right = img.Size.Width / 2 + (SettingsHolder.Instance.DZoneHor / 100 * img.Size.Width);
            double Top = img.Size.Height - (SettingsHolder.Instance.DZoneVert / 100 * img.Size.Height);
            double Bottom = img.Size.Height;
            DZrect = new Rectangle((int)Left, (int)Top, (int)(Right - Left), (int)(Bottom - Top));
            if (SettingsHolder.Instance.ShowDZ)
            {
                Emgu.CV.CvInvoke.Rectangle(img, DZrect, new MCvScalar(0, 0, 255));
            }
            for (int i = 0; i < lstDetect.Count; i++)
            {
                if (DZrect.Contains(lstDetect[i].GetCenter))
                {
                    CollisionWarningEvent(new CollisionEventArgs(lstDetect[i]));
                }
            }



        }

        private void DrawDetectionBoxes(ref Image<Bgr, byte> img, List<Detection> lstDetect)
        {
            for (int i = 0; i < lstDetect.Count; i++)
            {
                Emgu.CV.CvInvoke.Rectangle(img, lstDetect[i].BBox.ToRect(), new MCvScalar(0, 255, 0));
            }
        }

        private void FillList(List<Detection> lstDetect, float[] scores, int[] classes, float[] ymin, float[] xmin, float[] ymax, float[] xmax, Size imgsize)
        {
            for (int i = 0; i < scores.Length; i++)
            {

                if (scores[i] > SettingsHolder.Instance.ScoreThresh)
                {
                    RectangleF rect = new RectangleF(xmin[i] * imgsize.Width, ymin[i] * imgsize.Height, (ymax[i] - ymin[i]) * imgsize.Height, (xmax[i] - xmin[i]) * imgsize.Width);
                    double az = (imgsize.Width / 2 - rect.Center().X) * SettingsHolder.Instance.CamFOV / imgsize.Width;
                    lstDetect.Add(new Detection(scores[i], classes[i], rect, az));
                }
            }

        }

        private void button1_Click(object sender, EventArgs e)
        {

            if (thrPipe == null)
            {
                thrPipe = new Thread(run_server);
                thrPipe.IsBackground = true;
                thrPipe.Start();
            }
            else
            {
                cancelEvent.Cancel();
                while ((thrPipe.ThreadState & (System.Threading.ThreadState.Stopped | System.Threading.ThreadState.Unstarted | System.Threading.ThreadState.WaitSleepJoin | System.Threading.ThreadState.AbortRequested)) == 0)
                {

                    thrPipe.Abort();
                    thrPipeConnection.Abort();
                    Thread.Sleep(1);
                }
                cancelEvent = new CancellationTokenSource();
                thrPipe = new Thread(run_server);
                thrPipe.IsBackground = true;
                thrPipe.Start();

            }
            button1.Text = "Started!";

            //cap = new Emgu.CV.VideoCapture(@"rtsp://192.168.10.14/bs1");
            if (cap != null && cap.IsOpened)
            {
                cap.ImageGrabbed += imageGrabbedEvent;
                cap.Start();
            }
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SettingsForm setForm = new SettingsForm();
            setForm.Visible = true;
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            //close logic here
            //if(bHasPythonStarted)
            //{
            try
            {
                Process.GetProcessById(SettingsHolder.PythonProcessId).Kill();
            }
            catch (Exception ex)
            {

                Console.WriteLine(ex.Message);
            }
                
            //}
            
            base.OnClosing(e);
        }
    }

    static class ExtMethods
    {
        public static async Task WaitForConnectionExAsync(this NamedPipeServerStream stream, CancellationTokenSource canceltoken)
        {
            try
            {
                await stream.WaitForConnectionAsync(canceltoken.Token);

            }
            catch (Exception e)
            {
                throw e;
            }


        }

        public static Rectangle ToRect(this RectangleF rectf)
        {
            return new Rectangle((int)rectf.X, (int)rectf.Y, (int)rectf.Width, (int)rectf.Height);
        }
        public static Point Center(this RectangleF rectf)
        {
            return new Point((int)(rectf.X + (rectf.Width / 2)), (int)(rectf.Y + rectf.Height / 2));
        }
        public static Point Center(this Rectangle rectf)
        {
            return new Point((int)(rectf.X + (rectf.Width / 2)), (int)(rectf.Y + rectf.Height / 2));
        }

        public static ListBox.ObjectCollection TrimTo(this ListBox.ObjectCollection collection, int cnt)
        {
            int totalcnt = collection.Count;
            for (int i = 0; i < totalcnt - cnt; i++)
            {
                collection.RemoveAt(totalcnt - i-1);
            }
            return collection;
        }

        public static byte[] GetBytes(this string str)
        {
            return ASCIIEncoding.ASCII.GetBytes(str);
        }
        public static void Raise(this EventHandler handler, object sender, EventArgs args = null)
        {
            EventHandler localHandlerCopy = handler;
            if (args == null)
            {
                args = EventArgs.Empty;
            }
            if (localHandlerCopy != null)
            {
                localHandlerCopy(sender, args);
            }
        }

        public static void InvokeIfRequired(this ISynchronizeInvoke obj, MethodInvoker action)
        {
            if (obj.InvokeRequired)
            {
                object[] args = new object[0];
                obj.Invoke(action, args);
            }
            else
            {
                action();
            }
        }
    }

    static class CLI
    {
        private const int SW_MAXIMIZE = 3;
        private const int SW_MINIMIZE = 6;
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static void MinimizeWindow(IntPtr hwnd)
        {
            ShowWindow(hwnd, SW_MINIMIZE);
        }
        public static void RunProcess(ref bool IsStarted)
        {
            // Set working directory and create process
            DirectoryInfo curDir = new FileInfo(Application.ExecutablePath).Directory;
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = "cmd.exe";
            start.Arguments = "/K "+curDir+"\\run_script.bat";
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.WorkingDirectory = curDir.FullName;
            start.WindowStyle = ProcessWindowStyle.Minimized;
            string stdout, stderr;
            using (Process process = Process.Start(start))
            {
                long AffinityMask = (long)process.ProcessorAffinity;
                AffinityMask &= 0x0001; // use only any of the first 4 available processors
                process.ProcessorAffinity = (IntPtr)AffinityMask;
                SettingsHolder.PythonProcessId = process.Id;
                MinimizeWindow(Process.GetProcessById(process.Id).MainWindowHandle);
                IsStarted = true;
                using (StreamReader reader = process.StandardOutput)
                {
                    stdout = reader.ReadToEnd();
                }

                using (StreamReader reader = process.StandardError)
                {
                    stderr = reader.ReadToEnd();
                }
                
                process.WaitForExit();
            }
           
        }
    }
}
