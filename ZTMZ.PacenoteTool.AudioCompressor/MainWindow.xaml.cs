﻿using Microsoft.Win32;
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
        private int _adjustValue = 0;
        private bool _isCutHeadAndTail = false;
        private double _cutRatio = 0.2;
        private int _currentPlayAmplification = 0;

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
                            var wavFile = new AutoResampledCachedSound(file);

                            // cut head & tail
                            if (this._isCutHeadAndTail)
                            {
                                wavFile.CutHeadAndTail(this._cutRatio);
                            }
                            if (this._currentPlayAmplification != 0)
                            {
                                wavFile.Amplification = this._currentPlayAmplification;
                            }


                            var resampler = new MediaFoundationResampler(
                                new SampleToWaveProvider(new AutoResampledCachedSoundSampleProvider(wavFile)),
                                WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1));
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
                bgw.RunWorkerCompleted += (o, e) =>
                {
                    this.enableControls(true);
                    this.tb_status.Text = "完成压缩";
                };
                bgw.RunWorkerAsync();
            }
            catch 
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
                    return 128;
                case 2:
                    return 320;
                default:
                    return 48;
            }
        }

        private void enableControls(bool enable)
        {
            this.btn_GO.IsEnabled = enable;
            this.btn_Adjust.IsEnabled = enable;
            this.cb_audioQuality.IsEnabled = enable;
            this.cb_IsCopyNonAudioFiles.IsEnabled = enable;
            this.tbx_input.IsEnabled = enable;
            this.tbx_output.IsEnabled = enable;
            this.btn_input.IsEnabled = enable;
            this.btn_output.IsEnabled = enable;
        }

        private void Btn_Adjust_OnClick(object sender, RoutedEventArgs e)
        {
            var inputPath = this.tbx_input.Text;
            var audioTypes = from p in Config.Instance.SupportedAudioTypes select p.Replace("*", "");
            var bgw = new BackgroundWorker();
            bgw.DoWork += (o, e) =>
            {

                var filesCount = 0;
                List<Tuple<int, string>> files = new List<Tuple<int, string>>();
                foreach (string file in Directory.EnumerateFiles(inputPath, "*.*", SearchOption.AllDirectories))
                {
                    var ext = System.IO.Path.GetExtension(file);
                    if (audioTypes.Contains(ext))
                    {
                        var strDistance = System.IO.Path.GetFileNameWithoutExtension(file);
                        int distance = 0;
                        if (int.TryParse(strDistance, out distance))
                        {
                            filesCount++;
                            files.Add(new Tuple<int, string>(distance, file));
                        }
                    }
                }
                this.Dispatcher.Invoke(() =>
                {
                    this.enableControls(false);
                    this.pb_progress.Minimum = 0;
                    this.pb_progress.Maximum = filesCount;
                    this.pb_progress.Value = 0;
                });

                var current = 0;
                foreach (var fileTuple in files)
                {
                    var distance = fileTuple.Item1;
                    var file = fileTuple.Item2;
                    var ext = System.IO.Path.GetExtension(file);
                    File.Move(file,
                        System.IO.Path.Join(System.IO.Path.GetDirectoryName(file),
                            string.Format("{0}{1}", distance + this._adjustValue, ext)));
                    this.Dispatcher.Invoke(() =>
                    {
                        this.tb_status.Text = $"正在调整文件：{distance}.{ext}";
                        this.pb_progress.Value = ++current;
                    });
                }
            };

            bgw.RunWorkerCompleted += (o, e) =>
            {
                this.enableControls(true);
                this.tb_status.Text = "完成调整";
            };
            bgw.RunWorkerAsync();
        }

        private void txb_adjust_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            this._adjustValue = this.txb_adjust.Value.HasValue ? this.txb_adjust.Value.Value : 0;
        }

        private void chk_headAndTail_Click(object sender, RoutedEventArgs e)
        {
            this._isCutHeadAndTail = this.chk_headAndTail.IsChecked.HasValue ? this.chk_headAndTail.IsChecked.Value : true;
        }

        private void sl_headAndTail_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            this._cutRatio = this.sl_headAndTail.Value;
            if (this.chk_headAndTail != null) 
                this.chk_headAndTail.Content = $"{(this._cutRatio * 100).ToString("0")}%";
        }

        private void sl_volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var value = (int)e.NewValue;
            this._currentPlayAmplification = value;
            if (this.tb_volume != null)
            {
                this.tb_volume.Text = value > 0 ? $"+{value}" : $"{value}";
            }
        }
    }
}
