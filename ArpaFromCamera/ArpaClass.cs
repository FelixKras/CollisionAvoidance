using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ArpaFromCamera
{
    internal class ArpaMsgDTO
    {
        /*
            TTM

            $--TTM,xx,x.x,x.x,a,x.x,x.x,a,x.x,x.x,a,c--c,a,a,hhmmss.ss,a*hh
            TTM = Tracked target message (ARPA Radar only)
            
            1 = Target number, (00 - 99)
            2 = Target distance from own ship
            3 = Bearing from own ship
            4 = Target bearing is T / R (True / Relative)
            5 = Target Speed
            6 = Target Course
            7 = Target course is T / R (True / Relative)
            8 = Distance of Closest Point of Approach (CPA)
            9 = Time to CPA in minutes, positive is approaching target, ( - ) negative is moving away.
            10 = Speed / Distance units K / N / S (Kilometers / Knots / Statute miles)
            11 = User data - generally target name
            12 = Target Status L / Q / T (Lost from tracking process / Query - in process of acquisition / Tracking at the present time)
            13 = Reference target = R, null otherwise
            14 = Time of data in UTC format (hhmmss.ss)
            15 = Type of target acquisition A / M (Automatic / Manual)
            16 = Checksum 
            
            
             */



        internal int TargetNumber;
        internal double TargetBearing;
        internal double TargetDistance;
        internal char TargetBearingUnits;
        internal double TargetSpeed;
        internal double TargetCourse;
        internal char TargetCourseUnits;
        internal double TargetCPADist;
        internal double TargetCPATime;
        internal char TargetSpeedDistUnits;
        internal string TargetName;
        internal char TargetStatus;
        internal char TargetReference;
        internal double TargetUTC;
        internal char TargetAcqType;
        internal DateTime TargetTime;

        public override string ToString()
        {
            string result = string.Empty;
            TargetBearingUnits = 'T';
            TargetCourseUnits = 'K';
            TargetSpeedDistUnits = 'K';
            TargetCPADist = 0;
            TargetCPATime = 0;
            TargetStatus = 'T';
            TargetReference = ' ';
            result = string.Format("$RATTM,{0:D},{1:F1},{2:F1},{3},{4:F1},{5:F1},{6},{7:F1},{8:F1},{9},{10},{11},{12},{13},{14}",
                TargetNumber, TargetDistance, TargetBearing, TargetBearingUnits,
                TargetSpeed, TargetCourse, TargetCourseUnits, TargetCPADist, TargetCPATime, TargetSpeedDistUnits,
                TargetName, TargetStatus, TargetReference, TargetTime.ToString("hhmmss.ss"), "a*");

            result += CalcChecksum(result).ToString("X2") + "\r\n";

            return result;
        }

        private int CalcChecksum(string s)
        {
            int iChcksm = 0;
            s = s.Substring(1, s.IndexOf('*') - 1);
            for (int i = 0; i < s.Length; i++)
            {
                iChcksm ^= Convert.ToByte(s[i]);
            }
            return iChcksm;
        }
    }

    public static class ArpaClass
    {
        static UdpClient udpc;
        static Regex rgxIDRangeAz;
        static volatile int IsDataAvail;
        static ArpaMsgDTO[] arpaMsgs;
        readonly static object lockObject;

        public const string versionNumber = "1.0.0.3";
        public const string version = "Arpa receiving library: " + versionNumber;

        static ArpaClass()
        {
            lockObject = new object();
            rgxIDRangeAz = new Regex(@"(?<=#TARGETID#RNG#AZ)[-+\d.#]*");
        }
        public static bool Init()
        {
            bool bRes = false;
            try
            {

                arpaMsgs = new ArpaMsgDTO[10];
                udpc = new UdpClient(36666);
                udpc.BeginReceive(new AsyncCallback(OnUdpData), udpc);


                bRes = true;
            }
            catch (Exception ex)
            {
                bRes = false;

            }
            return bRes;
        }

        static void OnUdpData(IAsyncResult result)
        {

            UdpClient socket = result.AsyncState as UdpClient;

            IPEndPoint ipeFrom = null;

            byte[] message = socket.EndReceive(result, ref ipeFrom);
            ParseData(message);
            socket.BeginReceive(new AsyncCallback(OnUdpData), socket);

        }

        private static void ParseData(byte[] message)
        {
            string sData = ASCIIEncoding.ASCII.GetString(message);
            string[] s1Arpas = sData.Split(new string[] { "<EOL>" }, StringSplitOptions.RemoveEmptyEntries);
            lock (lockObject)
            {
                arpaMsgs = new ArpaMsgDTO[s1Arpas.Length];

                for (int ii = 0; ii < s1Arpas.Length; ii++)
                {
                    Match mtch = rgxIDRangeAz.Match(s1Arpas[ii]);
                    string[] split = mtch.Value.Split(new char[] { '#' },StringSplitOptions.RemoveEmptyEntries);
                    int ID = int.Parse(split[0]);
                    double Range = double.Parse(split[1]);
                    double Az = double.Parse(split[2]);

                    lock (lockObject)
                    {
                        arpaMsgs[ii] = new ArpaMsgDTO();
                        arpaMsgs[ii].TargetName = "OpticColAv_"+ID;
                        arpaMsgs[ii].TargetTime = DateTime.UtcNow;
                        arpaMsgs[ii].TargetDistance = Range;
                        arpaMsgs[ii].TargetBearing = Az;
                        arpaMsgs[ii].TargetSpeed = 0;
                        arpaMsgs[ii].TargetCourse = 0;
                        arpaMsgs[ii].TargetNumber = ii;
                        string str = arpaMsgs[ii].ToString();

                    }
                }
                Interlocked.Exchange(ref IsDataAvail, 1);
            }
        }
        public static string[] GetArpa(double Heading)
        {


            string[] result;
            if (Interlocked.CompareExchange(ref IsDataAvail, 0, 1) == 1)
            {
                lock (lockObject)
                {
                    result = new string[arpaMsgs.Length];
                    for (int ii = 0; ii < arpaMsgs.Length; ii++)
                    {
                        arpaMsgs[ii].TargetBearing += Heading; //regard 360, maybe put %
                        arpaMsgs[ii].TargetCourse = -arpaMsgs[ii].TargetBearing;
                        result[ii] = arpaMsgs[ii].ToString();
                    }
                }
            }
            else
            {
                result = new string[0];
            }


            return result;
        }

        public static void Dispose()
        {
            if (udpc != null)
            {
                udpc.Close();

            }
        }

        public static void Test()
        {
            ArpaClass.Init();
            UdpClient udpTest = new UdpClient();
            for (int i = 0; i < 9; i++)
            {
                string teststring = string.Format("#WARNING#TARGET#RNG#AZ#500.00{0:d}#10.66{0:d}", i, i);
                udpc.Send(ASCIIEncoding.ASCII.GetBytes(teststring), teststring.Length,
                    new IPEndPoint(IPAddress.Parse("127.0.0.1"), 36666));
            }


            while (true)
            {
                ArpaClass.GetArpa(10);
            }
        }

    }


}
