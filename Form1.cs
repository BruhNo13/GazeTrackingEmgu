using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace GazeTrackingEmgu
{
    public partial class Form1 : Form
    {
        

        private Stopwatch gazeTimer = new Stopwatch();
        private string lastDirection = "Monitor";
        private VideoCapture _videoCapture;
        private CascadeClassifier _faceCascade;
        private CascadeClassifier _eyeCascade;
        //private CascadeClassifier _profileFaceCascade;
        //private CascadeClassifier _leftEyeCascade;
        //private CascadeClassifier _rightEyeCascade;

        private bool _isRunning = false;

        public Form1()
        {
            InitializeComponent();

            _faceCascade = new CascadeClassifier("haarcascades/haarcascade_frontalface_default.xml");
            _eyeCascade = new CascadeClassifier("haarcascades/haarcascade_eye.xml");
            // _profileFaceCascade = new CascadeClassifier("haarcascades/haarcascade_profileface.xml");
            //_leftEyeCascade = new CascadeClassifier("haarcascades/haarcascade_lefteye_2splits.xml");
            //_rightEyeCascade = new CascadeClassifier("haarcascades/haarcascade_righteye_2splits.xml");

        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void startToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!_isRunning)
            {
                _videoCapture = new VideoCapture();
                _videoCapture.ImageGrabbed += ProcessFrame;
                _videoCapture.Start();
                _isRunning = true;
            }
        }

        private void stopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_isRunning)
            {
                _videoCapture.Stop();
                _videoCapture.Dispose();
                _videoCapture = null;
                _isRunning = false;
            }
        }

        private void pauseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_isRunning)
            {
                _videoCapture.Stop();
                _videoCapture.Dispose();
                _videoCapture = null;
                _isRunning = false;
            }
        }

        private void ProcessFrame(object sender, EventArgs e)
        {
            try
            {
                Mat frame = new Mat();
                _videoCapture.Retrieve(frame);

                using (Image<Bgr, byte> image = frame.ToImage<Bgr, byte>())
                {
                    UMat grayImage = new UMat();
                    CvInvoke.CvtColor(image, grayImage, ColorConversion.Bgr2Gray);

                    
                    Rectangle[] faces = _faceCascade.DetectMultiScale(grayImage, 1.1, 4);

                    foreach (Rectangle face in faces)
                    {
                        CvInvoke.Rectangle(image, face, new MCvScalar(0, 255, 0), 2); 

                        Mat faceROI = new Mat(frame, face);

                        
                        Rectangle[] eyes = _eyeCascade.DetectMultiScale(faceROI, 1.1, 4);

                        if (eyes.Length < 2)
                            continue; 

                        
                        var sortedEyes = eyes.OrderByDescending(e => e.Width * e.Height).Take(2).ToArray();
                        Rectangle leftEye = sortedEyes[0].X < sortedEyes[1].X ? sortedEyes[0] : sortedEyes[1];
                        Rectangle rightEye = sortedEyes[0].X < sortedEyes[1].X ? sortedEyes[1] : sortedEyes[0];

                        
                        leftEye = new Rectangle(face.X + leftEye.X, face.Y + leftEye.Y, leftEye.Width, leftEye.Height);
                        rightEye = new Rectangle(face.X + rightEye.X, face.Y + rightEye.Y, rightEye.Width, rightEye.Height);

                        
                        CvInvoke.Rectangle(image, leftEye, new MCvScalar(255, 0, 0), 2);
                        CvInvoke.Rectangle(image, rightEye, new MCvScalar(255, 0, 0), 2);

                       
                        Mat leftEyeROI = new Mat(frame, leftEye);
                        Mat rightEyeROI = new Mat(frame, rightEye);
                        DetectEyeDirection(image, leftEyeROI, face, leftEye, rightEye);
                        DetectEyeDirection(image, rightEyeROI, face, leftEye, rightEye);
                    }

                    pictureBox1.Invoke((MethodInvoker)(() =>
                    {
                        pictureBox1.Image = image.ToBitmap();
                    }));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Chyba: " + ex.Message);
            }
        }



        private void DetectEyeDirection(Image<Bgr, byte> image, Mat eyeROI, Rectangle face, Rectangle leftEye, Rectangle rightEye)
        {
            try
            {
                UMat grayEye = new UMat();
                CvInvoke.CvtColor(eyeROI, grayEye, ColorConversion.Bgr2Gray);
                CvInvoke.GaussianBlur(grayEye, grayEye, new Size(3, 3), 0);

                
                UMat thresholdEye = new UMat();
                CvInvoke.Threshold(grayEye, thresholdEye, 30, 255, ThresholdType.BinaryInv);

                using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                {
                    CvInvoke.FindContours(thresholdEye, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);
                    if (contours.Size > 0)
                    {
                        double maxArea = 0;
                        int maxIndex = -1;

                        for (int i = 0; i < contours.Size; i++)
                        {
                            double area = CvInvoke.ContourArea(contours[i]);
                            if (area > maxArea && area > 5) 
                            {
                                maxArea = area;
                                maxIndex = i;
                            }
                        }

                        if (maxIndex != -1)
                        {
                            Moments moments = CvInvoke.Moments(contours[maxIndex]);
                            if (moments.M00 != 0)
                            {
                                int centerX = (int)(moments.M10 / moments.M00);
                                int centerY = (int)(moments.M01 / moments.M00);
                                int eyeWidth = eyeROI.Width;
                                int eyeHeight = eyeROI.Height;

                                string direction = "Monitor";

                                
                                double pupilShiftX = (double)centerX / eyeWidth; 

                                if (pupilShiftX < 0.3)  
                                    direction = "Doprava";
                                else if (pupilShiftX > 0.7)  
                                    direction = "Do¾ava";

                               
                                double pupilShiftY = (double)centerY / eyeHeight;
                                if (centerY > eyeHeight * 0.6)
                                    direction = "Dole";
                                else if (centerY < eyeHeight * 0.3)
                                    direction = "Hore";

                                Console.WriteLine($"Smer poh¾adu: {direction}");

                                
                                if (direction == "Doprava" || direction == "Do¾ava") 
                                {
                                    if (lastDirection == direction)
                                    {
                                        if (!gazeTimer.IsRunning)
                                            gazeTimer.Start();

                                        if (gazeTimer.ElapsedMilliseconds > 1000) 
                                        {
                                            Invoke((MethodInvoker)(() =>
                                            {
                                                MessageBox.Show($"Upozornenie: {direction}!", "Gaze Tracking", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                            }));
                                            gazeTimer.Reset();
                                        }
                                    }
                                    else
                                    {
                                        lastDirection = direction;
                                        gazeTimer.Reset();
                                    }
                                }
                                else if (direction != "Monitor") 
                                {
                                    if (lastDirection == direction)
                                    {
                                        if (!gazeTimer.IsRunning)
                                            gazeTimer.Start();

                                        if (gazeTimer.ElapsedMilliseconds > 2000)
                                        {
                                            Invoke((MethodInvoker)(() =>
                                            {
                                                MessageBox.Show($"Upozornenie: {direction}!", "Gaze Tracking", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                            }));
                                            gazeTimer.Reset();
                                        }
                                    }
                                    else
                                    {
                                        lastDirection = direction;
                                        gazeTimer.Reset();
                                    }
                                }
                                else
                                {
                                    gazeTimer.Reset();
                                    lastDirection = "Monitor";
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Chyba pri detekcii poh¾adu: " + ex.Message);
            }
        }


    }
}
