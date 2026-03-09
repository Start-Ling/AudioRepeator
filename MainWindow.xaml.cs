using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AudioRepeator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // 音频采集相关
        private WaveInEvent _waveIn;
        private BufferedWaveProvider _bufferedWaveProvider;
        private WaveOutEvent _waveOut;

        // 音频缓存（用于播放）
        private List<byte> _audioBuffer = new List<byte>();
        private List<byte> _triggeredAudioBuffer = new List<byte>();

        // 频率分析相关
        private int _sampleRate = 44100; // 采样率
        private int _fftSize = 1024;     // FFT大小
        private int _frequencyThreshold; // 频率阈值
        private int _totalCount = 0;     // 总触发次数

        // 5秒窗口计数
        private Queue<DateTime> _triggerTimestamps = new Queue<DateTime>();
        private int _5sCount = 0;

        // UI更新定时器
        private DispatcherTimer _uiUpdateTimer;

        // 播放锁（防止重复播放）
        private bool _isPlaying = false;

        public MainWindow()
        {
            InitializeComponent();

            // 初始化UI更新定时器
            _uiUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
        }

        #region 按钮事件
        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // 验证频率阈值输入
            if (!int.TryParse(txtFrequencyThreshold.Text, out _frequencyThreshold) || _frequencyThreshold <= 0)
            {
                MessageBox.Show("请输入有效的频率阈值（正整数）", "输入错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // 初始化音频输入
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(_sampleRate, 1) // 单声道，44100Hz采样率
                };

                // 设置音频缓存
                _bufferedWaveProvider = new BufferedWaveProvider(_waveIn.WaveFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(10), // 缓存10秒音频
                    DiscardOnBufferOverflow = true
                };

                // 注册音频数据可用事件
                _waveIn.DataAvailable += WaveIn_DataAvailable;

                // 清空状态
                _totalCount = 0;
                _triggerTimestamps.Clear();
                _5sCount = 0;
                _audioBuffer.Clear();
                _triggeredAudioBuffer.Clear();

                // 启动采集
                _waveIn.StartRecording();
                _uiUpdateTimer.Start();

                // 更新UI状态
                btnStart.IsEnabled = false;
                btnStop.IsEnabled = true;
                txtStatus.Text = "正在监测...";
                txtStatus.Foreground = Brushes.Green;
                txtPlaybackTip.Text = "";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                btnStart.IsEnabled = true;
                btnStop.IsEnabled = false;
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopMonitoring();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopMonitoring();
            _waveOut?.Dispose();
        }
        #endregion

        #region 音频处理核心逻辑
        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            // 将音频数据写入缓存
            _bufferedWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
            _audioBuffer.AddRange(e.Buffer.Take(e.BytesRecorded));

            // 限制缓存大小（最多保存5秒音频）
            int maxBufferSize = _sampleRate * 2 * 5; // 2字节/采样 * 5秒
            if (_audioBuffer.Count > maxBufferSize)
            {
                _audioBuffer.RemoveRange(0, _audioBuffer.Count - maxBufferSize);
            }

            // 转换音频数据为浮点数用于FFT分析
            float[] audioSamples = new float[e.BytesRecorded / 2];
            for (int i = 0; i < audioSamples.Length; i++)
            {
                // 将16位PCM数据转换为-1到1之间的浮点数
                audioSamples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
            }

            // 进行FFT分析获取主频率
            float dominantFrequency = GetDominantFrequency(audioSamples);

            // 检查是否超过阈值
            if (dominantFrequency >= _frequencyThreshold)
            {
                _totalCount++;

                // 记录触发时间戳
                lock (_triggerTimestamps)
                {
                    _triggerTimestamps.Enqueue(DateTime.Now);
                    // 保留5秒内的时间戳
                    while (_triggerTimestamps.Count > 0 && DateTime.Now - _triggerTimestamps.Peek() > TimeSpan.FromSeconds(5))
                    {
                        _triggerTimestamps.Dequeue();
                    }
                    _5sCount = _triggerTimestamps.Count;
                }

                // 保存触发时的音频缓存
                _triggeredAudioBuffer = new List<byte>(_audioBuffer);

                // 检查是否需要播放
                if (_5sCount > 2 && !_isPlaying)
                {
                    Dispatcher.Invoke(() =>
                    {
                        txtPlaybackTip.Text = "5秒内触发超过2次，正在播放音频...";
                    });
                    PlayAudioBuffer();
                }
            }

            // 更新当前频率（UI线程）
            Dispatcher.Invoke(() =>
            {
                txtCurrentFrequency.Text = $"{Math.Round(dominantFrequency)} Hz";

                // 绘制简易波形
                DrawWaveform(audioSamples);
            });
        }

        /// <summary>
        /// 通过FFT获取音频的主频率
        /// </summary>
        private float GetDominantFrequency(float[] samples)
        {
            if (samples.Length < _fftSize) return 0;

            // 准备FFT数据
            Complex[] fftBuffer = new Complex[_fftSize];
            for (int i = 0; i < _fftSize; i++)
            {
                fftBuffer[i].X = samples[i] * (float)FastFourierTransform.HammingWindow(i, _fftSize);
                fftBuffer[i].Y = 0;
            }

            // 执行FFT
            FastFourierTransform.FFT(true, (int)Math.Log(_fftSize, 2), fftBuffer);

            // 找到能量最大的频率
            int maxIndex = 0;
            float maxValue = 0;

            // 只分析前半部分（奈奎斯特频率）
            for (int i = 0; i < _fftSize / 2; i++)
            {
                float magnitude = (float)Math.Sqrt(fftBuffer[i].X * fftBuffer[i].X + fftBuffer[i].Y * fftBuffer[i].Y);
                if (magnitude > maxValue)
                {
                    maxValue = magnitude;
                    maxIndex = i;
                }
            }

            // 计算实际频率
            return (float)maxIndex * _sampleRate / _fftSize;
        }

        /// <summary>
        /// 播放缓存的音频数据
        /// </summary>
        private void PlayAudioBuffer()
        {
            if (_triggeredAudioBuffer.Count == 0 || _isPlaying) return;

            try
            {
                _isPlaying = true;
                _waveOut?.Dispose();
                _waveOut = new WaveOutEvent();
                // 创建内存流播放音频
                var memoryStream = new System.IO.MemoryStream(_triggeredAudioBuffer.ToArray());
                var waveStream = new RawSourceWaveStream(memoryStream, _waveIn.WaveFormat);

                _waveOut.Init(waveStream);
                _waveOut.Play();
                var count = 0;
                // 播放完成后重置状态
                _waveOut.PlaybackStopped += (s, e) =>
                {
                    count++;
                    if (count < 3)
                    {
                        waveStream.Seek(0, System.IO.SeekOrigin.Begin);
                        _waveOut.Play();
                    }
                    else
                    {
                        _isPlaying = false;
                        waveStream.Dispose();
                        memoryStream.Dispose();
                        Dispatcher.Invoke(() =>
                        {
                            txtPlaybackTip.Text = "音频播放完成";
                        });
                        _waveOut?.Dispose();
                        _waveOut = null;
                    }
                };
            }
            catch (Exception ex)
            {
                _isPlaying = false;
                Dispatcher.Invoke(() =>
                {
                    txtPlaybackTip.Text = $"播放失败：{ex.Message}";
                });
            }
        }
        #endregion

        #region UI辅助方法
        private void UiUpdateTimer_Tick(object sender, EventArgs e)
        {
            // 更新计数显示
            txtTotalCount.Text = _totalCount.ToString();
            txt5sCount.Text = _5sCount.ToString();

            // 清理过期的时间戳
            lock (_triggerTimestamps)
            {
                while (_triggerTimestamps.Count > 0 && DateTime.Now - _triggerTimestamps.Peek() > TimeSpan.FromSeconds(5))
                {
                    _triggerTimestamps.Dequeue();
                }
                _5sCount = _triggerTimestamps.Count;
            }
        }

        /// <summary>
        /// 绘制简易音频波形
        /// </summary>
        private void DrawWaveform(float[] samples)
        {
            waveformCanvas.Children.Clear();
            if (samples.Length == 0) return;

            int width = (int)waveformCanvas.ActualWidth;
            int height = (int)waveformCanvas.ActualHeight;
            int centerY = height / 2;

            // 采样绘制（避免过多点）
            int step = Math.Max(1, samples.Length / width);
            Pen pen = new Pen(Brushes.DodgerBlue, 1);

            for (int i = 0; i < width && i * step < samples.Length; i++)
            {
                float sampleValue = samples[i * step];
                double y = centerY - (sampleValue * centerY * 0.8);

                Line line = new Line
                {
                    X1 = i,
                    Y1 = centerY,
                    X2 = i,
                    Y2 = y,
                    Stroke = pen.Brush,
                    StrokeThickness = pen.Thickness
                };

                waveformCanvas.Children.Add(line);
            }
        }

        /// <summary>
        /// 停止监测
        /// </summary>
        private void StopMonitoring()
        {
            // 停止播放
            if (_waveOut?.PlaybackState == PlaybackState.Playing)
            {
                _waveOut?.Stop();
            }

            // 停止采集
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveIn = null;

            // 停止UI更新
            _uiUpdateTimer.Stop();

            // 更新UI状态
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
            txtStatus.Text = "已停止";
            txtStatus.Foreground = Brushes.Gray;
            txtPlaybackTip.Text = "";

            // 清空波形
            waveformCanvas.Children.Clear();
        }
        #endregion

    }
}
