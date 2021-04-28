using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CollisionAvoidance
{
    public partial class SettingsForm : Form
    {
        public SettingsForm()
        {
            InitializeComponent();
            SettingsHolder.LoadFromJson();
            propertyGrid1.SelectedObject = SettingsHolder.Instance;
            
        }

        private void fSettings_FormClosing(object sender, FormClosingEventArgs e)
        {
            //update?
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string jsonstring = JsonConvert.SerializeObject(SettingsHolder.Instance);
            using (FileStream fs = new FileStream("settings.json", FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite))
            using (StreamWriter sw = new StreamWriter(fs))
            {
                sw.Write(jsonstring);
            }

        }

        private void loadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SettingsHolder.LoadFromJson();
            propertyGrid1.SelectedObject = SettingsHolder.Instance;
        }

       
    }
}
