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
        private int gazeWarnings = 0; 
        private int falseMonitorCount = 0;
        private Stopwatch fpsTimer = new Stopwatch();
        private int frameCount = 0;
        private double fps = 0;
        private bool showBoundingBoxes = true;
        private bool _isRunning = false;

        public Form1()
        {
            InitializeComponent();
            _faceCascade = new CascadeClassifier("haarcascades/haarcascade_frontalface_default.xml");
            _eyeCascade = new CascadeClassifier("haarcascades/haarcascade_eye.xml");
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

                    if (showBoundingBoxes)
                        CvInvoke.Rectangle(image, face, new MCvScalar(0, 255, 0), 2);

                    Mat faceROI = new Mat(frame, face);
                    Rectangle[] eyes = _eyeCascade.DetectMultiScale(faceROI, 1.1, 4);
                    if (eyes.Length < 2) return;

                    var sortedEyes = eyes.OrderByDescending(e => e.Width * e.Height).Take(2).ToArray();
                    Rectangle leftEye = sortedEyes[0].X < sortedEyes[1].X ? sortedEyes[0] : sortedEyes[1];
                    Rectangle rightEye = sortedEyes[0].X < sortedEyes[1].X ? sortedEyes[1] : sortedEyes[0];

                    int reducedHeight = (int)(leftEye.Height * 0.55); 
                    int reducedWidth = (int)(leftEye.Width * 0.15);
                    leftEye = new Rectangle(face.X + leftEye.X + reducedWidth, face.Y + leftEye.Y + reducedHeight / 2, leftEye.Width - (2 * reducedWidth), reducedHeight);
                    rightEye = new Rectangle(face.X + rightEye.X + reducedWidth, face.Y + rightEye.Y + reducedHeight / 2, rightEye.Width - (2 * reducedWidth), reducedHeight);

                    if (showBoundingBoxes)
                    {
                        CvInvoke.Rectangle(image, leftEye, new MCvScalar(255, 0, 0), 2);
                        CvInvoke.Rectangle(image, rightEye, new MCvScalar(255, 0, 0), 2);
                    }

                    Mat leftEyeROI = new Mat(frame, leftEye);
                    Mat rightEyeROI = new Mat(frame, rightEye);
                    DetectEyeDirection(image, leftEyeROI, leftEye);
                    DetectEyeDirection(image, rightEyeROI, rightEye);

                    frameCount++;
                    if (!fpsTimer.IsRunning)
                        fpsTimer.Start();
                    else if (fpsTimer.ElapsedMilliseconds >= 1000)
                    {
                        fps = frameCount / (fpsTimer.ElapsedMilliseconds / 1000.0);
                        frameCount = 0;
                        fpsTimer.Restart();
                    }

                    string fpsText = $"FPS: {fps:F1}";
                    CvInvoke.PutText(image, fpsText, new Point(image.Width - 120, 30), FontFace.HersheySimplex, 0.7, new MCvScalar(0, 255, 0), 2);

                    pictureBox1.Invoke((MethodInvoker)(() =>
                    {
                        pictureBox1.Image = image.ToBitmap();
                    }));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
        }

        private void DetectEyeDirection(Image<Bgr, byte> image, Mat eyeROI, Rectangle eyeRect)
        {
            try
            {
                UMat grayEye = new UMat();
                CvInvoke.CvtColor(eyeROI, grayEye, ColorConversion.Bgr2Gray);

                CvInvoke.EqualizeHist(grayEye, grayEye);

                double minVal = 0, maxVal = 0;
                Point minLoc = new Point(), maxLoc = new Point();
                CvInvoke.MinMaxLoc(grayEye, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

                UMat thresholdEye = new UMat();
                CvInvoke.Threshold(grayEye, thresholdEye, (minVal + 30), 255, ThresholdType.BinaryInv);

                using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                {
                    CvInvoke.FindContours(thresholdEye, contours, null, RetrType.External, ChainApproxMethod.ChainApproxSimple);

                    if (contours.Size == 0)
                    {
                        Console.WriteLine("No contours found!");
                        return;
                    }

                    double maxArea = 0;
                    int maxIndex = -1;
                    Rectangle boundingRect = new Rectangle();

                    for (int i = 0; i < contours.Size; i++)
                    {
                        double area = CvInvoke.ContourArea(contours[i]);
                        Rectangle tempRect = CvInvoke.BoundingRectangle(contours[i]);

                        if (area > 50 && area < eyeROI.Width * 0.25 * eyeROI.Height)
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
                        int centerX, centerY;

                        if (moments.M00 != 0)
                        {
                            centerX = (int)(moments.M10 / moments.M00);
                            centerY = (int)(moments.M01 / moments.M00);
                        }
                        else
                        {
                            centerX = boundingRect.X + boundingRect.Width / 2;
                            centerY = boundingRect.Y + boundingRect.Height / 2;
                        }

                        if (centerX < boundingRect.X || centerX > boundingRect.X + boundingRect.Width)
                        {
                            centerX = boundingRect.X + boundingRect.Width / 2;
                        }
                        if (centerY < boundingRect.Y || centerY > boundingRect.Y + boundingRect.Height)
                        {
                            centerY = boundingRect.Y + boundingRect.Height / 2;
                        }

                        centerX += eyeRect.X;
                        centerY += eyeRect.Y;

                        if (showBoundingBoxes)
                            CvInvoke.Circle(image, new Point(centerX, centerY), 3, new MCvScalar(255, 0, 0), -1);

                        int eyeWidth = eyeROI.Width;
                        int eyeHeight = eyeROI.Height;
                        string direction = "Screen";

                        double pupilShiftX = (double)(centerX - eyeRect.X) / eyeWidth;
                        if (pupilShiftX < 0.42) 
                            direction = "Right";
                        else if (pupilShiftX > 0.58) 
                            direction = "Left";

                        double pupilShiftY = (double)(centerY - eyeRect.Y) / eyeHeight;
                        if (pupilShiftY > 0.75) 
                            direction = "Down";
                        else if (pupilShiftY < 0.25) 
                            direction = "Up";

                        Console.WriteLine($"Gaze direction: {direction}");


                        if (direction != "Screen") 
                        {
                            if (!gazeTimer.IsRunning)
                            {
                                gazeTimer.Start();
                                falseMonitorCount = 0; 
                            }
                            else if (gazeTimer.ElapsedMilliseconds > 3000) 
                            {
                                MessageBox.Show("Look at your screen!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                gazeTimer.Reset(); 
                            }
                        }
                        else 
                        {
                            if (gazeTimer.IsRunning)
                            {
                                falseMonitorCount++; 

                                if (falseMonitorCount >= 10) 
                                {
                                    gazeTimer.Reset(); 
                                    falseMonitorCount = 0;
                                }
                            }
                        }

                        if (showBoundingBoxes)
                        {
                            CvInvoke.Rectangle(image, eyeRect, new MCvScalar(0, 255, 0), 2);

                            int leftThreshold = eyeRect.X + (int)(eyeWidth * 0.42);
                            int rightThreshold = eyeRect.X + (int)(eyeWidth * 0.58);
                            int topThreshold = eyeRect.Y + (int)(eyeHeight * 0.25);
                            int bottomThreshold = eyeRect.Y + (int)(eyeHeight * 0.75);

                            CvInvoke.Line(image, new Point(leftThreshold, eyeRect.Y), new Point(leftThreshold, eyeRect.Y + eyeHeight), new MCvScalar(0, 0, 255), 2);
                            CvInvoke.Line(image, new Point(rightThreshold, eyeRect.Y), new Point(rightThreshold, eyeRect.Y + eyeHeight), new MCvScalar(0, 0, 255), 2);
                            CvInvoke.Line(image, new Point(eyeRect.X, topThreshold), new Point(eyeRect.X + eyeWidth, topThreshold), new MCvScalar(0, 0, 255), 2);
                            CvInvoke.Line(image, new Point(eyeRect.X, bottomThreshold), new Point(eyeRect.X + eyeWidth, bottomThreshold), new MCvScalar(0, 0, 255), 2);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while detecting gaze direction: " + ex.Message);
            }
        }

        private void toggleBoundingBoxesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showBoundingBoxes = !showBoundingBoxes;
            toggleBoundingBoxesToolStripMenuItem.Text = showBoundingBoxes ? "Hide boxes" : "Show boxes";
        }
    }
}
