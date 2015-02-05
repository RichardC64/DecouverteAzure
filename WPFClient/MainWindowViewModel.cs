using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AForge.Video;
using AForge.Video.DirectShow;
using AForge.Vision.Motion;
using Newtonsoft.Json;

namespace WPFClient
{
    public class MainWindowViewModel : ViewModelBase, IDisposable
    {
        private FilterInfoCollection _videoDevices;
        private VideoCaptureDevice _videoSource;
        private MotionDetector _motionDetector;
        private readonly SynchronizationContext _synchronizationContext;
        private List<FilterInfo> _devices;
        private FilterInfo _selectedDevice;
        private BitmapImage _currentImage;
        private bool _bitmapToUpload;
        private readonly DispatcherTimer _timer;
        private string _information;
        private ICommand _startCommand;
        private string _azureSiteUrl;

        public MainWindowViewModel()
        {
            _synchronizationContext = SynchronizationContext.Current;
            _timer = new DispatcherTimer();
            _timer.Tick += OnTimerTick;
            _timer.Interval = new TimeSpan(0, 0, 0, (int) Properties.Settings.Default.Duration);
            AzureSiteUrl = Properties.Settings.Default.AzureSiteUrl;
        }

        //-----------------public properties
        public List<FilterInfo> Devices
        {
            get { return _devices; }
            set
            {
                _devices = value;
                OnPropertyChanged();
            }
        }
        public FilterInfo SelectedDevice
        {
            get { return _selectedDevice; }
            set
            {
                _selectedDevice = value;
                OnPropertyChanged();
            }
        }
        public BitmapImage CurrentImage
        {
            get { return _currentImage; }
            set
            {
                _currentImage = value;
                OnPropertyChanged();
            }
        }
        public string Information
        {
            get { return _information; }
            set
            {
                _information = value;
                OnPropertyChanged();
            }
        }
        public string AzureSiteUrl
        {
            get { return _azureSiteUrl; }
            set
            {
                _azureSiteUrl = value;
                OnPropertyChanged();
            }
        }

        public ICommand StartCommand
        {
            get { return _startCommand ?? (_startCommand = new RelayCommand(Start)); }
        }

        //-----------------public methods
        public void InitializeWebCamList()
        {
            SelectedDevice = null;
            Devices = null;

            _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            if (_videoDevices.Count == 0)
                return;

            Devices = _videoDevices.Cast<FilterInfo>().ToList();
            SelectedDevice = Devices[0];
        }

        //-----------------private methods
        private void Start()
        {
            if (SelectedDevice == null)
                return;

            CloseVideoSource();
            // le détecteur de mouvement
            _motionDetector = new MotionDetector(
                new TwoFramesDifferenceDetector
                {
                    DifferenceThreshold = 15,
                    SuppressNoise = true
                },
                new BlobCountingObjectsProcessing
                {
                    HighlightColor = Color.Red,
                    HighlightMotionRegions = true,
                    MinObjectsHeight = 10,
                    MinObjectsWidth = 10
                });

            _videoSource = new VideoCaptureDevice(SelectedDevice.MonikerString);
            _videoSource.NewFrame += OnNewFrameReceived;
            _videoSource.Start();
            _timer.IsEnabled = true;
        }

        private async void OnTimerTick(object sender, EventArgs e)
        {
            if (CurrentImage == null || !_bitmapToUpload) return;

            Uri siteUri;
            if (!Uri.TryCreate(AzureSiteUrl, UriKind.Absolute, out siteUri))
            {
                Information = "Url du site Azure non valide !";
                return;
            }
            var uri = new Uri("/Images/UploadImage", UriKind.Relative);
            var azureWebSiteUri = new Uri(siteUri, uri);
            
            Information = "Sauvegarde en cours...";
            _timer.Stop();

            var bitmap = ToBitmap(CurrentImage);
            if (bitmap == null)
            {
                Information = "";
                _bitmapToUpload = false;
                return;
            }

            var bitmapDatas = (byte[]) new ImageConverter().ConvertTo(bitmap, typeof (byte[]));

            var request = (HttpWebRequest)WebRequest.Create(azureWebSiteUri);
            request.Method = "POST";

            // traitement de la requête
            using (var stream = await Task.Factory.FromAsync<Stream>(
                request.BeginGetRequestStream,
                request.EndGetRequestStream, null))
            {
                await stream.WriteAsync(bitmapDatas, 0, bitmapDatas.Length);
            }

            // traitement de la réponse
            try
            {
                var response = await Task.Factory.FromAsync<WebResponse>(
                    request.BeginGetResponse,
                    request.EndGetResponse, null);

                Information = "";
                _bitmapToUpload = false;
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    var serialiser = new JsonSerializer();
                    var configuration = (AppConfiguration) serialiser.Deserialize(reader, typeof (AppConfiguration));

                    if (Properties.Settings.Default.Duration != configuration.Duration)
                    {
                        Information = string.Format("Changement de la fréquence de téléchargement : {0}s", configuration.Duration);
                        Properties.Settings.Default.Duration = configuration.Duration;
                        
                        _timer.Interval = new TimeSpan(0, 0, 0, Properties.Settings.Default.Duration);
                    }
                    Properties.Settings.Default.AzureSiteUrl = azureWebSiteUri.ToString();
                    Properties.Settings.Default.Save();

                    _timer.Start();
                }
            }
            catch (Exception ex)
            {
                Information = ex.Message;
                _bitmapToUpload = false;
            }
        }

        private void CloseVideoSource()
        {
            if (_motionDetector != null) _motionDetector.Reset();
            if (_videoSource == null) return;
            if (!_videoSource.IsRunning) return;
            _videoSource.SignalToStop();
            _videoSource = null;
        }
        private void OnNewFrameReceived(object sender, NewFrameEventArgs eventArgs)
        {
            var img = (Bitmap) eventArgs.Frame.Clone();
            var motionLevel = _motionDetector.ProcessFrame(img);

            if (CurrentImage == null)
            {
                _synchronizationContext.Post(
                    o =>
                    {
                        CurrentImage = ToBitmapImage(img);
                    }, null);
            }
            if (motionLevel < .005f) return;

            _bitmapToUpload = true;
            _synchronizationContext.Post(o =>
            {
                Information = "Mouvement détecté !";
                CurrentImage = ToBitmapImage(img);
            }, null);
        }
        private BitmapImage ToBitmapImage(Image bitmap)
        {
            var bitmapImage = new BitmapImage();
            using (var mem = new MemoryStream())
            {
                bitmap.Save(mem, ImageFormat.Jpeg);
                mem.Position = 0;

                bitmapImage.BeginInit();
                bitmapImage.StreamSource = mem;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
            }
            return bitmapImage;
        }
        private Bitmap ToBitmap(BitmapImage bitmapImage)
        {
            using (var mem = new MemoryStream())
            {
                var encoder = new JpegBitmapEncoder();
                encoder.QualityLevel = 70;
                encoder.Frames.Add(BitmapFrame.Create(bitmapImage.Clone()));
                encoder.Save(mem);
                var bmp = new Bitmap(mem);
                return new Bitmap(bmp);
            }
        }

        #region Dispose
        protected virtual void Dispose(bool disposing)
        {
            CloseVideoSource();
            if (disposing)
            {
            }
        }
        ~MainWindowViewModel()
        {
            Dispose(false);
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

    }
}
