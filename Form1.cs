using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using Emgu.CV;
using Emgu.CV.Structure;

namespace GazeTrackingEmgu
{
    public partial class Form1 : Form
    {

        VideoCapture VideoCapture;

        public Form1()
        {
            InitializeComponent();
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void startToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (VideoCapture == null)
            {
                VideoCapture = new VideoCapture();
            }
            VideoCapture.ImageGrabbed += VideoCapture_ImageGrabbed1;
            VideoCapture.Start();
        }

        private void VideoCapture_ImageGrabbed1(object? sender, EventArgs e)
        {
            try
            {
                Mat m = new Mat();
                VideoCapture.Retrieve(m);
                pictureBox1.Image = m.ToImage<Bgr, Byte>().AsBitmap();
            }
            catch (Exception)
            {

            }
        }
        private void VideoCapture_ImageGrabbed(object? sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void stopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (VideoCapture != null)
            {
                VideoCapture = null;
            }
        }

        private void pauseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (VideoCapture != null)
            {
                VideoCapture.Pause();
            }
        }
    }
}
