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

namespace GrabFrame
{
    public partial class frmMain : Form
    {
        VideoCapture cap;
        Mat inputFrame = new Mat();
        CancellationTokenSource cancelEvent;
        Thread thrPipe;
        Thread thrPipeConnection;
        NamedPipeServerStream server;

        public frmMain()
        {
            InitializeComponent();
            cancelEvent = new CancellationTokenSource();
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

        void run_server()
        {
            // Open the named pipe.
            if (server == null)
            {
                server = new NamedPipeServerStream("DetectionData", PipeDirection.InOut, 2);
            }
            else
            {
                server.Close();

            }
            thrPipeConnection = new Thread(() => { server.WaitForConnection(); });
            thrPipeConnection.Start();
            while ((thrPipeConnection.ThreadState & ThreadState.Stopped) == 0 || cancelEvent.IsCancellationRequested)
            {

            }







            var br = new BinaryReader(server);
            var bw = new BinaryWriter(server);


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
                    byte[] classBytes= br.ReadBytes(len4);
                    byte[] Out_yminBytes = br.ReadBytes(len4 * sizeof(float));
                    byte[] Out_xminBytes = br.ReadBytes(len4 * sizeof(float));
                    byte[] Out_ymaxBytes = br.ReadBytes(len4 * sizeof(float));
                    byte[] Out_xmaxBytes = br.ReadBytes(len4 * sizeof(float));

                    byte[,,] imgBytes3d = new byte[len1, len2, len3];
                    Buffer.BlockCopy(imgBytes, 0, imgBytes3d, 0, imgBytes.Length);
                    Image<Bgr, byte> img = new Image<Bgr, byte>(imgBytes3d);
                    pictureBox1.Image = img.ToBitmap();
                    Console.WriteLine("Read: \"{0}\" bytes", imgBytes.Length);

                   
                    float[] Scores = new float[len4];
                    float[] ymin = new float[len4];
                    float[] xmin = new float[len4];
                    float[] ymax = new float[len4];
                    float[] xmax = new float[len4];

                    Buffer.BlockCopy(imgBytes, 0, imgBytes3d, 0, imgBytes.Length);
                    Buffer.BlockCopy(scoresBytes, 0, Scores, 0, scoresBytes.Length);
                    Buffer.BlockCopy(Out_yminBytes, 0, ymin, 0, scoresBytes.Length);
                    Buffer.BlockCopy(Out_xminBytes, 0, xmin, 0, scoresBytes.Length);
                    Buffer.BlockCopy(Out_ymaxBytes, 0, ymax, 0, scoresBytes.Length);
                    Buffer.BlockCopy(Out_xmaxBytes, 0, xmax, 0, scoresBytes.Length);
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
                while ((thrPipe.ThreadState & (ThreadState.Stopped | ThreadState.Unstarted | ThreadState.WaitSleepJoin | ThreadState.AbortRequested)) == 0)
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
    }
}
