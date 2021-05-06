using Emgu.CV;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
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
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Emgu.CV.CvEnum;

namespace CollisionAvoidance
{
    public partial class frmMain : Form
    {
        private VideoCapture cap;
        private Mat inputFrame = new Mat();
        private CancellationTokenSource cancelEvent;
        private Thread thrServer;
        private Thread thrPipeConnection;
        private NamedPipeServerStream server;
        private bool bHasPythonStarted;
        private ConcurrentStack<Image<Bgr, byte>> grabbedImages;
        private TimeSpan perfTime;
        private EventHandler TargetsReceived;
        private EventHandler CollisionWarning;
        private readonly object _lockobj;
        internal class Detection
        {
            internal float Score;
            internal int Class;
            internal string ClassName;
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

            public Detection(float v, int c, RectangleF rectangleF, double az, string s)
            {
                this.Score = v;
                this.Class = c;
                this.BBox = rectangleF;
                this.Azimuth = az;
                this.ClassName = s;
            }
        }

        internal class TargetsReceivedEventArgs : EventArgs
        {
            internal Detection[] colDetect;
            internal TimeSpan TimeFromLastDetect;
            public TargetsReceivedEventArgs()
            {

            }
            public TargetsReceivedEventArgs(Detection[] det, TimeSpan lastDetect)
            {
                this.colDetect = det;
                this.TimeFromLastDetect = lastDetect;
            }
        }

        internal class CollisionEventArgs : EventArgs
        {
            internal Detection[] colDetect;
            internal TimeSpan TimeFromLastDetect;
            public CollisionEventArgs()
            {

            }
            public CollisionEventArgs(Detection[] det)
            {
                this.colDetect = det;
            }
        }



        public frmMain()
        {

            InitializeComponent();
            this.Text = Program.version;
            cancelEvent = new CancellationTokenSource();
            _lockobj = new object();
            CollisionWarning += onCollisionHandler;
            TargetsReceived += onTargetsReceived;
            bHasPythonStarted = false;

            SettingsHolder.LoadFromJson();
            SettingsHolder.PythonProcessId = -1;
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && args[1].ToLowerInvariant().Contains("auto"))
            {
                StartDetection();
                button1.Enabled = false;
            }
        }

        private void onCollisionHandler(object sender, EventArgs e)
        {
            if (e is CollisionEventArgs ecol)
            {
                for (int ii = 0; ii < ecol.colDetect.Length; ii++)
                {
                    OnReceivedMessage(string.Format("Collision detected. AZ: {0:F3}", ecol.colDetect[ii].Azimuth), EventArgs.Empty);
                }
                SendUdpMessage(ecol.colDetect);
            }
        }

        private void onTargetsReceived(object sender, EventArgs e)
        {
            if (e is TargetsReceivedEventArgs etar)
            {
                string sMsg = string.Format("Received {0:D} targets above thresh ({1:F1})",
                    etar.colDetect.Length, SettingsHolder.Instance.ScoreThresh);
                lock (_lockobj)
                {
                    perfTime = etar.TimeFromLastDetect;
                }
                OnReceivedMessage(sMsg, EventArgs.Empty);
            }
        }

        private void imageGrabbedEvent(object sender, EventArgs eargs)
        {

            try
            {
                cap.Retrieve(inputFrame);
                Image<Bgr, byte> latestAcquiredImage = inputFrame.ToImage<Bgr, byte>();
                if (grabbedImages?.Count > 1)
                {
                    grabbedImages.Clear();
                }
                grabbedImages?.Push(latestAcquiredImage);

            }
            catch (Exception e)
            {

            }
        }

        private void SendUdpMessage(Detection[] colDetect)
        {
            UdpClient udpc = new UdpClient();
            IPEndPoint remoteIPE = new IPEndPoint(IPAddress.Parse(SettingsHolder.Instance.IPAddress), SettingsHolder.Instance.IPPort);
            string msg = string.Empty;
            for (int ii = 0; ii < colDetect.Length; ii++)
            {
                msg += string.Format("#WARNING#TARGETID#RNG#AZ#{0:D}#{1:F3}#{2:F3}<EOL>", colDetect[ii].Class, SettingsHolder.Instance.VDistance, colDetect[ii].Azimuth);

            }

            udpc.Send(msg.GetBytes(), msg.Length, remoteIPE);
            TargetsReceived.Raise("Warning Sent");
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

                        lock (_lockobj)
                        {
                            textBox1.Text = (perfTime.TotalMilliseconds/1000D).ToString("F3");
                        }

                    });
                //lstMessages.Insert(0, sMsgToDisplay);
            }
        }

        private void StartDetection()
        {
            if (SettingsHolder.Instance.UsePythonTF)
            {
                StartPipeServer();
            }
            else
            {
                StartTFServer();
            }

            button1.Text = "Started!";
        }

        void run_server()
        {

            if (!bHasPythonStarted)
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

                    byte[] Classes = new byte[len4];
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

                    if (SettingsHolder.Instance.ShowBoxes)
                    {
                        DrawDetectionBoxes(ref img, lstDetect);
                    }

                    TargetsReceived.Raise(null, new TargetsReceivedEventArgs(lstDetect.ToArray(), TimeSpan.FromMilliseconds(99999)));
                    List<Detection> dangerTargets = CheckIfInDangerZone(ref img, lstDetect);
                    if (dangerTargets.Count > 0)
                    {
                        CollisionWarning.Raise(null, new CollisionEventArgs(dangerTargets.ToArray()));
                    }


                    pictureBox1.Image = img.ToBitmap();
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

        void run_TFdetector()
        {
            TFDetect.TFDetect.Init(SettingsHolder.Instance.ModelFile, SettingsHolder.Instance.LabellFile);
            Image<Bgr, byte> grabbedImage = null;
            while (!cancelEvent.IsCancellationRequested)
            {

                if (grabbedImages != null && grabbedImages.TryPop(out grabbedImage) && grabbedImage.Width * grabbedImage.Height > 0)
                {
                    TFDetect.Results resultsFromTF = null;
                    var sw = Stopwatch.StartNew();
                    TFDetect.TFDetect.Predict(grabbedImage.ToBitmap(), ref resultsFromTF);
                    sw.Stop();

                    /*
                    Rectangle rect = new Rectangle()
                    {
                        X = (int)left,
                        Y = (int)top,
                        Width = (int)(right - left),
                        Height = (int)(bottom - top)
                    };
                    */




                    List<Detection> lstDetect = new List<Detection>();
                    FillList(lstDetect, resultsFromTF.Scores, resultsFromTF.ClassesID,
                        resultsFromTF.top, resultsFromTF.left, resultsFromTF.right,
                        resultsFromTF.bottom, grabbedImage.Size, resultsFromTF.ClassesName);


                    TargetsReceived.Raise(null, new TargetsReceivedEventArgs(lstDetect.ToArray(), TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds)));

                    List<Detection> dangerTargets = CheckIfInDangerZone(ref grabbedImage, lstDetect);
                    if (dangerTargets.Count > 0)
                    {
                        CollisionWarning.Raise(null, new CollisionEventArgs(dangerTargets.ToArray()));
                    }
                    if (SettingsHolder.Instance.ShowBoxes)
                    {
                        DrawDetectionBoxes(ref grabbedImage, lstDetect);
                    }
                    pictureBox1.Image = grabbedImage.ToBitmap();
                }
                else
                {
                    Thread.Sleep(0);
                }


            }
            cancelEvent = new CancellationTokenSource();
        }

        private BinaryReader InitPipe()
        {
            // Open the named pipe.
            if (server == null)
            {
                server = new NamedPipeServerStream("DetectionData", PipeDirection.InOut, 2);
                TargetsReceived.Raise(string.Format("Started module connection"));
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

        private List<Detection> CheckIfInDangerZone(ref Image<Bgr, byte> img, List<Detection> lstDetect)
        {
            bool bRes = false;
            Rectangle DZrect = Rectangle.Empty;
            double Left = img.Size.Width / 2 - (SettingsHolder.Instance.DZoneHor / 100 * img.Size.Width);
            double Right = img.Size.Width / 2 + (SettingsHolder.Instance.DZoneHor / 100 * img.Size.Width);
            double Top = img.Size.Height - (SettingsHolder.Instance.DZoneVert / 100 * img.Size.Height);
            double Bottom = img.Size.Height;
            DZrect = new Rectangle((int)Left, (int)Top, (int)(Right - Left), (int)(Bottom - Top));
            List<Detection> lstDangerTarg = new List<Detection>();
            if (SettingsHolder.Instance.ShowDZ)
            {
                Emgu.CV.CvInvoke.Rectangle(img, DZrect, new MCvScalar(0, 0, 255));
            }
            for (int i = 0; i < lstDetect.Count; i++)
            {
                if (DZrect.Contains(lstDetect[i].GetCenter) && lstDangerTarg.Count < SettingsHolder.Instance.NumberOfDangerTargets)
                {
                    lstDangerTarg.Add(lstDetect[i]);

                }
            }
            if (lstDangerTarg.Count > 0)
            {
                bRes = true;
            }

            return lstDangerTarg;
        }

        private void DrawDetectionBoxes(ref Image<Bgr, byte> img, List<Detection> lstDetect)
        {
            for (int i = 0; i < lstDetect.Count; i++)
            {
                Emgu.CV.CvInvoke.Rectangle(img, lstDetect[i].BBox.ToRect(), new MCvScalar(0, 255, 255));
                Point p = new Point(lstDetect[i].BBox.ToRect().Right + 5, lstDetect[i].BBox.ToRect().Top + 5);
                string text = String.Empty;
                if (string.IsNullOrEmpty(lstDetect[i].ClassName))
                {
                    text = string.Format("[id:{0}]:{1}%", lstDetect[i].Class, (int)(lstDetect[i].Score * 100));
                }
                else
                {
                    text = string.Format("{0}:{1}%", lstDetect[i].ClassName, (int)(lstDetect[i].Score * 100));
                }

                Emgu.CV.CvInvoke.PutText(img, text, p, FontFace.HersheyPlain, 1, new MCvScalar(0, 255, 255));

            }
        }
        private void DrawDetectionBoxes2(Bitmap bmp, Rectangle rect, float score, string name)
        {
            using (Graphics graphic = Graphics.FromImage(bmp))
            {
                graphic.SmoothingMode = SmoothingMode.AntiAlias;

                using (Pen pen = new Pen(Color.Red, 2))
                {
                    graphic.DrawRectangle(pen, rect);

                    Point p = new Point(rect.Right + 5, rect.Top + 5);
                    string text = string.Format("{0}:{1}%", name, (int)(score * 100));
                    graphic.DrawString(text, new Font("Verdana", 8), Brushes.Red, p);
                }
            }
        }
        private void FillList(List<Detection> lstDetect, float[] scores, byte[] classes, float[] ymin, float[] xmin, float[] ymax, float[] xmax, Size imgsize, string[] Names = null)
        {
            for (int i = 0; i < scores.Length; i++)
            {

                if (scores[i] > SettingsHolder.Instance.ScoreThresh)
                {
                    RectangleF rect = new RectangleF(xmin[i] * imgsize.Width, ymin[i] * imgsize.Height, (ymax[i] - ymin[i]) * imgsize.Height, (xmax[i] - xmin[i]) * imgsize.Width);
                    double az = (imgsize.Width / 2 - rect.Center().X) * SettingsHolder.Instance.CamFOV / imgsize.Width;
                    if (Names == null)
                    {
                        lstDetect.Add(new Detection(scores[i], classes[i], rect, az));
                    }
                    else
                    {
                        lstDetect.Add(new Detection(scores[i], classes[i], rect, az, Names[i]));
                    }

                }
            }

        }

        private void button1_Click(object sender, EventArgs e)
        {
            StartDetection();
        }

        private void StartPipeServer()
        {
            if (thrServer == null)
            {
                thrServer = new Thread(run_server);
                thrServer.IsBackground = true;
                thrServer.Start();
            }
            else
            {
                cancelEvent.Cancel();
                while ((thrServer.ThreadState & (System.Threading.ThreadState.Stopped | System.Threading.ThreadState.Unstarted |
                                               System.Threading.ThreadState.WaitSleepJoin |
                                               System.Threading.ThreadState.AbortRequested)) == 0)
                {
                    Thread.Sleep(1);
                }

                cancelEvent = new CancellationTokenSource();
                thrServer = new Thread(run_server);
                thrServer.IsBackground = true;
                thrServer.Start();
            }
        }
        private void StartTFServer()
        {
            bool bRes = false;
            int CamNum = -1;
            if (int.TryParse(SettingsHolder.Instance.VidStream, out CamNum))
            {
                cap = new Emgu.CV.VideoCapture(CamNum);
            }
            else
            {
                cap = new Emgu.CV.VideoCapture(SettingsHolder.Instance.VidStream);
            }

            if (cap != null && cap.IsOpened)
            {
                if (grabbedImages == null)
                {
                    grabbedImages = new ConcurrentStack<Image<Bgr, byte>>();
                }
                cap.ImageGrabbed += imageGrabbedEvent;
                cap.Start();
                bRes = true;
            }
            else
            {
                bRes = false;
            }

            if (bRes)
            {
                if (thrServer == null)
                {
                    thrServer = new Thread(run_TFdetector);
                    thrServer.IsBackground = true;
                    thrServer.Priority = ThreadPriority.BelowNormal;
                    thrServer.Start();
                }
                else
                {
                    cancelEvent.Cancel();
                    cap.Stop();
                    while ((thrServer.ThreadState & (System.Threading.ThreadState.Stopped | System.Threading.ThreadState.Unstarted |
                                                     System.Threading.ThreadState.WaitSleepJoin |
                                                     System.Threading.ThreadState.AbortRequested)) == 0)
                    {
                        Thread.Sleep(1);
                    }

                    cancelEvent = new CancellationTokenSource();
                    thrServer = new Thread(run_TFdetector)
                    { IsBackground = true, Priority = ThreadPriority.BelowNormal };
                    thrServer.Start();
                }

                if (SettingsHolder.Instance.SingleCoreProcessing)
                {
                    SetCPUCoreAffinity();
                }

            }


        }

        private void SetCPUCoreAffinity()
        {
            Process process = Process.GetCurrentProcess();
            long affinityMask = (long)process.ProcessorAffinity;
            // 0xfff    = 1111 1111 1111
            // 0x0001   = 0000 0000 0001
            affinityMask &= 0x0001; // use only any of the first 4 available processors
            process.Threads[thrServer.ManagedThreadId].ProcessorAffinity = (IntPtr)(affinityMask);
            process.ProcessorAffinity = (IntPtr)(affinityMask);
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
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
                collection.RemoveAt(totalcnt - i - 1);
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
            start.Arguments = "/K " + curDir + "\\run_script.bat";
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
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
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
