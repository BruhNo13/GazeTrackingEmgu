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
                _videoCapture = new VideoCapture(0);
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
                    if (faces.Length == 0) return;

                    Rectangle face = faces.OrderByDescending(f => f.Width * f.Height).First();
                    if (face.Width < image.Width / 6) return;

                    CvInvoke.Rectangle(image, face, new MCvScalar(0, 255, 0), 2);

                    Mat faceROI = new Mat(frame, face);
                    Rectangle[] eyes = _eyeCascade.DetectMultiScale(faceROI, 1.1, 4);
                    if (eyes.Length < 2) return;

                    var sortedEyes = eyes.OrderByDescending(e => e.Width * e.Height).Take(2).ToArray();
                    Rectangle leftEye = sortedEyes[0].X < sortedEyes[1].X ? sortedEyes[0] : sortedEyes[1];
                    Rectangle rightEye = sortedEyes[0].X < sortedEyes[1].X ? sortedEyes[1] : sortedEyes[0];

                    // **Zmenšíme výšku oblasti oka**
                    int reducedHeight = (int)(leftEye.Height * 0.55); // Berieme iba spodnú polovicu oka
                    leftEye = new Rectangle(face.X + leftEye.X, face.Y + leftEye.Y + reducedHeight / 2, leftEye.Width, reducedHeight);
                    rightEye = new Rectangle(face.X + rightEye.X, face.Y + rightEye.Y + reducedHeight / 2, rightEye.Width, reducedHeight);

                    CvInvoke.Rectangle(image, leftEye, new MCvScalar(255, 0, 0), 2);
                    CvInvoke.Rectangle(image, rightEye, new MCvScalar(255, 0, 0), 2);

                    DrawEyeThresholdLines(image, leftEye);
                    DrawEyeThresholdLines(image, rightEye);

                    Mat leftEyeROI = new Mat(frame, leftEye);
                    Mat rightEyeROI = new Mat(frame, rightEye);
                    DetectEyeDirection(image, leftEyeROI, leftEye);
                    DetectEyeDirection(image, rightEyeROI, rightEye);

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


        private void DetectEyeDirection(Image<Bgr, byte> image, Mat eyeROI, Rectangle eyeRect)
        {
            try
            {
                UMat grayEye = new UMat();
                CvInvoke.CvtColor(eyeROI, grayEye, ColorConversion.Bgr2Gray);

                CvInvoke.EqualizeHist(grayEye, grayEye);

                CvInvoke.GaussianBlur(grayEye, grayEye, new Size(5, 5), 0);

                UMat thresholdEye = new UMat();
                CvInvoke.Threshold(grayEye, thresholdEye, 50, 255, ThresholdType.BinaryInv);

                using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                {
                    CvInvoke.FindContours(thresholdEye, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);

                    if (contours.Size == 0)
                    {
                        Console.WriteLine("Žiadne kontúry neboli nájdené!");
                        return;
                    }

                    double maxArea = 0;
                    int maxIndex = -1;
                    Rectangle boundingRect = new Rectangle();

                    for (int i = 0; i < contours.Size; i++)
                    {
                        double area = CvInvoke.ContourArea(contours[i]);
                        Rectangle tempRect = CvInvoke.BoundingRectangle(contours[i]);

                        if (area > 50 && area < eyeROI.Width * eyeROI.Height * 0.4)
                        {
                            if (area > maxArea)
                            {
                                maxArea = area;
                                maxIndex = i;
                                boundingRect = tempRect;
                            }
                        }
                    }

                    if (maxIndex != -1)
                    {
                        Moments moments = CvInvoke.Moments(contours[maxIndex]);
                        if (moments.M00 != 0)
                        {
                            int centerX = (int)(moments.M10 / moments.M00);
                            int centerY = (int)(moments.M01 / moments.M00);

                            CvInvoke.Circle(image, new Point(eyeRect.X + centerX, eyeRect.Y + centerY), 3, new MCvScalar(255, 0, 0), -1);

                            int eyeWidth = eyeROI.Width;
                            int eyeHeight = eyeROI.Height;
                            string direction = "Monitor";

                            double pupilShiftX = (double)centerX / eyeWidth;
                            if (pupilShiftX < 0.3)
                                direction = "Doprava";
                            else if (pupilShiftX > 0.7)
                                direction = "Do¾ava";

                            double pupilShiftY = (double)centerY / eyeHeight;
                            if (pupilShiftY > 0.6)
                                direction = "Dole";
                            else if (pupilShiftY < 0.3)
                                direction = "Hore";

                            Console.WriteLine($"Smer poh¾adu: {direction}");

                            CvInvoke.Rectangle(image, eyeRect, new MCvScalar(0, 255, 0), 2);

                            CvInvoke.Rectangle(image, new Rectangle(eyeRect.X + boundingRect.X, eyeRect.Y + boundingRect.Y, boundingRect.Width, boundingRect.Height), new MCvScalar(255, 0, 0), 2);
                        }
                    }
                    else
                    {
                        Console.WriteLine("Zrenica nebola nájdená v kontúrach!");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Chyba pri detekcii poh¾adu: " + ex.Message);
            }
        }





        private void DrawEyeThresholdLines(Image<Bgr, byte> image, Rectangle eye)
        {
            int leftThreshold = eye.X + (int)(eye.Width * 0.35);
            int rightThreshold = eye.X + (int)(eye.Width * 0.65);
            int topThreshold = eye.Y + (int)(eye.Height * 0.3);
            int bottomThreshold = eye.Y + (int)(eye.Height * 0.6);

            CvInvoke.Line(image, new Point(leftThreshold, eye.Y), new Point(leftThreshold, eye.Y + eye.Height), new MCvScalar(0, 0, 255), 2);
            CvInvoke.Line(image, new Point(rightThreshold, eye.Y), new Point(rightThreshold, eye.Y + eye.Height), new MCvScalar(0, 0, 255), 2);
            CvInvoke.Line(image, new Point(eye.X, topThreshold), new Point(eye.X + eye.Width, topThreshold), new MCvScalar(0, 0, 255), 2);
            CvInvoke.Line(image, new Point(eye.X, bottomThreshold), new Point(eye.X + eye.Width, bottomThreshold), new MCvScalar(0, 0, 255), 2);
        }



    }
}
