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
    public partial class Form1 : Form
    {
        VideoCapture cap;
        Mat inputFrame = new Mat();
        CancellationTokenSource cancelEvent;
        Thread thrPipe;
        Thread thrPipeConnection;
        NamedPipeServerStream server;

        public Form1()
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

            server = new NamedPipeServerStream("DetectionData");
            server.WaitForConnection();

            var br = new BinaryReader(server);
            var bw = new BinaryWriter(server);


            try
            {
                while (true)
                {
                    var len1 = (int)br.ReadUInt32();            // Read string length
                    var len2 = (int)br.ReadUInt32();
                    var len3 = (int)br.ReadUInt32();
                    byte[] imgBytes = br.ReadBytes(len1 * len2 * len3);
                    byte[,,] imgBytes3d = new byte[len1, len2, len3];
                    Buffer.BlockCopy(imgBytes, 0, imgBytes3d, 0, imgBytes.Length);
                    Image<Bgr, byte> img = new Image<Bgr, byte>(imgBytes3d);
                    pictureBox1.Image = img.ToBitmap();
                    Console.WriteLine("Read: \"{0}\" bytes", imgBytes.Length);

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


            thrPipe = new Thread(run_server);
            thrPipe.IsBackground = true;
            thrPipe.Start();

            button1.Text = "Started";

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
