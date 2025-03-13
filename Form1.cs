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
        //private Stopwatch gazeTimer = new Stopwatch();
        //private string lastDirection = "Monitor";
        private VideoCapture _videoCapture;
        private CascadeClassifier _faceCascade;
        private CascadeClassifier _eyeCascade;
        //private int gazeWarnings = 0;
        //private int falseMonitorCount = 0;
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
                _videoCapture.ImageGrabbed += ProcessFrameFromCamera;
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

        private void playVideoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string videoPath = "D:\\bakalarka\\GazeTrackingEmgu\\videos/gaze_tracking_test.mp4";
            string csvPath = "D:\\bakalarka\\GazeTrackingEmgu\\videos/gaze_direction.csv";
            Dictionary<int, string> gazeData = LoadGazeData(csvPath);
            VideoCapture video = new VideoCapture(videoPath);

            if (!video.IsOpened)
            {
                MessageBox.Show("Couldn't open video!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int frameNumber = 0;
            int correctDetections = 0;
            int totalFrames = Math.Min(1772, (int)video.Get(Emgu.CV.CvEnum.CapProp.FrameCount));

            while (frameNumber < totalFrames)
            {
                Mat frame = new Mat();
                video.Read(frame);

                string detectedGaze = ProcessFrame(frame, out Image<Bgr, byte> processedImage);
                string expectedGaze = gazeData.ContainsKey(frameNumber) ? gazeData[frameNumber] : "unknown";
                if (expectedGaze != "center") expectedGaze = "away";

                bool correct = detectedGaze == expectedGaze;
                if (correct) correctDetections++;
                frameNumber++;

                textBoxGazeStatus.Invoke((MethodInvoker)(() =>
                {
                    textBoxGazeStatus.Text = $"Frame: {frameNumber}/{totalFrames} | Detectede: {detectedGaze} | Expected: {expectedGaze} | {(correct ? "yes" : "no")}";
                }));

                pictureBox1.Invoke((MethodInvoker)(() =>
                {
                    if (processedImage != null)
                    {
                        pictureBox1.Image = processedImage.Resize(pictureBox1.Width, pictureBox1.Height, Emgu.CV.CvEnum.Inter.Linear).ToBitmap();
                    }
                }));

                Application.DoEvents();
            }

            video.Release();

            double accuracy = (double)correctDetections / totalFrames * 100;
            MessageBox.Show($"Analysis completed!\nCorrect detections: {correctDetections}/{totalFrames}\nAccuracy: {accuracy:F2}%",
                            "Results", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private string ProcessFrame(Mat frame, out Image<Bgr, byte> processedImage)
        {
            processedImage = frame.ToImage<Bgr, byte>();

            try
            {
                UMat grayImage = new UMat();
                CvInvoke.CvtColor(processedImage, grayImage, ColorConversion.Bgr2Gray);

                Rectangle[] faces = _faceCascade.DetectMultiScale(grayImage, 1.1, 4);
                if (faces.Length == 0) return "away";

                Rectangle face = faces.OrderByDescending(f => f.Width * f.Height).First();
                if (face.Width < processedImage.Width / 6) return "away";

                if (showBoundingBoxes)
                    CvInvoke.Rectangle(processedImage, face, new MCvScalar(0, 255, 0), 2);

                Mat faceROI = new Mat(processedImage.Mat, face);
                Rectangle[] eyes = _eyeCascade.DetectMultiScale(faceROI, 1.1, 4);
                if (eyes.Length < 2) return "away";

                var sortedEyes = eyes.OrderByDescending(e => e.Width * e.Height).Take(2).ToArray();
                Rectangle leftEye = sortedEyes[0].X < sortedEyes[1].X ? sortedEyes[0] : sortedEyes[1];
                Rectangle rightEye = sortedEyes[0].X < sortedEyes[1].X ? sortedEyes[1] : sortedEyes[0];

                int reducedHeight = (int)(leftEye.Height * 0.55);
                int reducedWidth = (int)(leftEye.Width * 0.15);

                leftEye = new Rectangle(face.X + leftEye.X + reducedWidth, face.Y + leftEye.Y + reducedHeight / 2, leftEye.Width - (2 * reducedWidth), reducedHeight);
                rightEye = new Rectangle(face.X + rightEye.X + reducedWidth, face.Y + rightEye.Y + reducedHeight / 2, rightEye.Width - (2 * reducedWidth), reducedHeight);

                string leftGaze = DetectEyeDirection(processedImage, new Mat(processedImage.Mat, leftEye), leftEye);
                string rightGaze = DetectEyeDirection(processedImage, new Mat(processedImage.Mat, rightEye), rightEye);
                string detectedGaze = leftGaze == rightGaze ? leftGaze : "away";

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
                CvInvoke.PutText(processedImage, fpsText, new Point(processedImage.Width - 120, 30), FontFace.HersheySimplex, 0.7, new MCvScalar(0, 255, 0), 2);


                return detectedGaze;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in ProcessFrame(): " + ex.Message);
            }

            return "away";
        }


        private void ProcessFrameFromCamera(object sender, EventArgs e)
        {
            try
            {
                Mat frame = new Mat();
                _videoCapture.Retrieve(frame);

                string gazeResult = ProcessFrame(frame, out Image<Bgr, byte> processedImage);

                textBoxGazeStatus.Invoke((MethodInvoker)(() =>
                {
                    textBoxGazeStatus.Text = "Gaze direction: " + gazeResult;
                }));

                pictureBox1.Invoke((MethodInvoker)(() =>
                {
                    pictureBox1.Image = processedImage.ToBitmap(); 
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while processing live video: " + ex.Message);
            }
        }

        private string DetectEyeDirection(Image<Bgr, byte> image, Mat eyeROI, Rectangle eyeRect)
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

                    if (contours.Size == 0) return "away";
                    

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
                        string direction = "center"; 

                        double pupilShiftX = (double)(centerX - eyeRect.X) / eyeWidth;
                        if (pupilShiftX < 0.42 || pupilShiftX > 0.58)  
                            direction = "away";

                        double pupilShiftY = (double)(centerY - eyeRect.Y) / eyeHeight;
                        if (pupilShiftY > 0.75 || pupilShiftY < 0.25)  
                            direction = "away";

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

                        return direction;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while detecting gaze direction: " + ex.Message);
            }

            return "away";
        }

        private void toggleBoundingBoxesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            showBoundingBoxes = !showBoundingBoxes;
            toggleBoundingBoxesToolStripMenuItem.Text = showBoundingBoxes ? "Hide boxes" : "Show boxes";
        }

        private Dictionary<int, string> LoadGazeData(string csvPath)
        {
            Dictionary<int, string> gazeData = new Dictionary<int, string>();
           
            using (StreamReader sr = new StreamReader(csvPath))
            {
                sr.ReadLine();

                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    line = line.Trim().Trim('"');
                    string[] parts = line.Split(',');

                    string frameString = parts[0].Trim().Trim('"');
                    string gazeDirection = parts[1].Trim().Trim('"').ToLower(); 
                    int frame = int.Parse(frameString);

                    gazeData[frame] = gazeDirection;

                    Console.WriteLine($"Loaded: Frame {frame} -> {gazeDirection}");
                }
            }
            return gazeData;
        }



    }
}
