using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.AudioCompressor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _isCopyNonAudioFiles = true;
        public MainWindow()
        {
            InitializeComponent();
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (this.cb_IsCopyNonAudioFiles.IsChecked.HasValue)
            {
                this._isCopyNonAudioFiles = this.cb_IsCopyNonAudioFiles.IsChecked.Value;
            }
        }

        private void btn_GO_Click(object sender, RoutedEventArgs e)
        {
            var inputPath = this.tbx_input.Text;
            var outputPath = this.tbx_output.Text;
            var sampleRate = getSampleRate();
            var bitRate = getBitRate();
            var audioTypes = from p in Config.Instance.SupportedAudioTypes select p.Replace("*", "");

            try
            {
                var bgw = new BackgroundWorker();
                bgw.DoWork += (o, e) =>
                {
                    if (!Directory.Exists(outputPath))
                    {
                        Directory.CreateDirectory(outputPath);
                    }
                    var filesCount = 0;

                    foreach (string file in Directory.EnumerateFiles(inputPath, "*.*", SearchOption.AllDirectories))
                    {

                        if (audioTypes.Contains(System.IO.Path.GetExtension(file)))
                        {
                            filesCount++;
                        }
                        else if (this._isCopyNonAudioFiles)
                        {
                            filesCount++;
                        }
                    }

                    this.Dispatcher.Invoke(() =>
                    {
                        this.pb_progress.Maximum = filesCount;
                        this.pb_progress.Minimum = 0;
                        this.enableControls(false);
                    });

                    var currentCount = 0;
                    foreach (string file in Directory.EnumerateFiles(inputPath, "*.*", SearchOption.AllDirectories))
                    {
                        var relatedFile = file.Replace(inputPath, "");
                        var outputFile = System.IO.Path.Join(outputPath, relatedFile);
                        var outputFilePath = System.IO.Path.GetDirectoryName(outputFile);
                        var outputFileName = System.IO.Path.GetFileName(outputFile);
                        if (!Directory.Exists(outputFilePath))
                        {
                            Directory.CreateDirectory(outputFilePath);
                        }

                        if (audioTypes.Contains(System.IO.Path.GetExtension(file)))
                        {
                            // is audio file
                            outputFile = System.IO.Path.GetFileNameWithoutExtension(outputFile) + ".mp3";
                            outputFile = System.IO.Path.Join(outputFilePath, outputFile);
                            using (var wavFile = new AudioFileReader(file))
                            {
                                var resampler = new MediaFoundationResampler(
                                    new SampleToWaveProvider(wavFile),
                                    WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2));
                                //var tmp = System.IO.Path.GetTempFileName();
                                //WaveFileWriter.CreateWaveFile(tmp, resampler);
                                //using (var waveReader = new WaveFileReader(tmp))
                                //{
                                MediaFoundationEncoder.EncodeToMp3(resampler, outputFile, bitRate);
                                this.Dispatcher.Invoke(() =>
                                {
                                    this.tb_status.Text = "正在压缩音频：" + outputFileName;
                                    this.pb_progress.Value = ++currentCount;
                                });
                                //}
                                //File.Delete(tmp);
                                //var wholeFile = new List<float>((int)(wavFile.Length / 4));
                                //var buffer = new byte[sampleRate * 2];
                                //int samplesRead;
                                //while ((samplesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
                                //{
                                //    mp3Writer.Write(buffer, 0, samplesRead);
                                //}
                            }
                        }
                        else if (this._isCopyNonAudioFiles)
                        {
                            // just copy
                            this.Dispatcher.Invoke(() =>
                            {
                                this.tb_status.Text = "正在复制文件：" + outputFileName;
                                this.pb_progress.Value = ++currentCount;
                            });
                            if (!File.Exists(outputFile))
                            {
                                File.Copy(file, outputFile);
                            }
                        }
                    }
                };
                bgw.RunWorkerAsync();
                bgw.RunWorkerCompleted += (o, e) =>
                {
                    this.enableControls(true);
                    this.tb_status.Text = "完成";
                };

            }
            catch (Exception ex)
            {

            }
        }

        private void btn_output_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            CommonFileDialogResult result = dialog.ShowDialog();
            if (result == CommonFileDialogResult.Ok)
            {
                this.tbx_output.Text = dialog.FileName;
            }
        }

        private void btn_input_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            CommonFileDialogResult result = dialog.ShowDialog();
            if (result == CommonFileDialogResult.Ok)
            {
                this.tbx_input.Text = dialog.FileName;
            }
        }

        private int getSampleRate()
        {
            switch (this.cb_audioQuality.SelectedIndex)
            {
                case 0:
                    return 11025;
                case 1:
                    return 22050;
                case 2:
                    return 44100;
                default:
                    return 11025;
            }
        }

        private int getBitRate()
        {
            switch (this.cb_audioQuality.SelectedIndex)
            {
                case 0:
                    return 48;
                case 1:
                    return 96;
                case 2:
                    return 320;
                default:
                    return 48;
            }
        }

        private void enableControls(bool enable)
        {
            this.btn_GO.IsEnabled = enable;
            this.cb_audioQuality.IsEnabled = enable;
            this.cb_IsCopyNonAudioFiles.IsEnabled = enable;
            this.tbx_input.IsEnabled = enable;
            this.tbx_output.IsEnabled = enable;
            this.btn_input.IsEnabled = enable;
            this.btn_output.IsEnabled = enable;
        }
    }
}
