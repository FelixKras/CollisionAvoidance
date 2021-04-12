
using Ookii.Dialogs.WinForms;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Design;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CollisionAvoidance
{
    static class Program
    {

        public const string versionNumber = "1.0.1.6";
        public const string version = "Collision avoidance app: " + versionNumber;
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new frmMain());
        }
    }

    public class SettingsHolder : IDisposable
    {

        private static SettingsHolder instance;
        private static object syncroot = new Object();
        public static int PythonProcessId { get; set; }

        public static SettingsHolder Instance
        {
            get
            {
                // If the instance is null then create one
                if (instance == null)
                {
                    lock (syncroot)
                    {
                        if (instance == null)
                        {
                            instance = new SettingsHolder();
                            instance.IPAddress = "127.0.0.1";
                            instance.IPPort = 36666;
                            instance.VidStream = "rtsp://192.168.10.14/bs1";
                            instance.VDistance = 500;
                            instance.ScoreThresh = 0.5;
                            instance.CamFOV = 20;
                            instance.DZoneVert = 60;
                            instance.DZoneHor = 35;
                            instance.ShowBoxes = true;
                            instance.ShowDZ = true;
                            instance.NumberOfDangerTargets = 10;
                        }
                    }
                }
                return instance;
            }

        }


        public static void Update(SettingsHolder inst)
        {
            instance = inst;
        }
        private SettingsHolder()
        {

        }


        [Category("1. Communication Properties")]
        [DisplayName("IP address")]
        [ReadOnly(false)]
        [Description("Address for avoidance message")]
        public string IPAddress { get; set; }

        [Category("1. Communication Properties")]
        [DisplayName("IP port")]
        [ReadOnly(false)]
        [Description("IP port for avoidance message")]
        public int IPPort { get; set; }

        [Category("1. Communication Properties")]
        [DisplayName("Video stream address")]
        [ReadOnly(true)]
        [Description("Address of camera stream - not in use")]
        public string VidStream { get; set; }


        [Category("2. Collision Avoidance")]
        [DisplayName("Virtual Distance")]
        [ReadOnly(false)]
        [Description("Virtual Distance of obstacle [m]")]
        public double VDistance { get; set; }

        [Category("3. Detection module")]
        [DisplayName("Score threshold")]
        [ReadOnly(false)]
        [Description("filter detections uder this score")]
        public double ScoreThresh { get; set; }

        [Category("3. Detection module")]
        [DisplayName("Camera field of view")]
        [ReadOnly(false)]
        [Description("Camera field of view  [°]")]
        public double CamFOV { get; set; }

        [Category("3. Detection module")]
        [DisplayName("Danger zone: horizontal")]
        [ReadOnly(false)]
        [Description("Horizontal angle around center to treat as a danger [percent of FOV]")]
        public double DZoneHor { get; set; }

        [Category("3. Detection module")]
        [DisplayName("Danger zone vertical")]
        [ReadOnly(false)]
        [Description("Vertical angle from buttom to treat as a danger [percent of FOV]")]
        public double DZoneVert { get; set; }

        [Category("3. Detection module")]
        [DisplayName("Number of targets")]
        [ReadOnly(false)]
        [Description("Number of targets in danger zone to send as arpa")]
        public int NumberOfDangerTargets { get;  set; }


        [Category("4. UI options")]
        [DisplayName("Show detection boxes")]
        [ReadOnly(false)]
        [Description("Show detection boxes")]
        public bool ShowBoxes { get; set; }

        [Category("4. UI options")]
        [DisplayName("Show danger zone")]
        [ReadOnly(false)]
        [Description("Show danger zone")]
        public bool ShowDZ { get; set; }
        
        public void Dispose()
        {
            lock (syncroot)
            {
                instance = null;
            }
        }

        internal class myFileBrowser : UITypeEditor
        {
            public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
            {
                return UITypeEditorEditStyle.Modal;
            }

            public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
            {
                using (Ookii.Dialogs.WinForms.VistaFileDialog ofd = new VistaOpenFileDialog())
                {
                    string[] s1Descript = context.PropertyDescriptor.Description.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    ofd.Filter = @"|*.csv";

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        return ofd.FileName;
                    }
                }
                return value;

            }
        }
    }
}
