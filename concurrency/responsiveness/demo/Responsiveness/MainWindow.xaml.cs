﻿// ----------------------------------------------------------------------------------------------
// Copyright (c) Mårten Rånge.
// ----------------------------------------------------------------------------------------------
// This source code is subject to terms and conditions of the Microsoft Public License. A
// copy of the license can be found in the License.html file at the root of this distribution.
// If you cannot locate the  Microsoft Public License, please send an email to
// dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound
//  by the terms of the Microsoft Public License.
// ----------------------------------------------------------------------------------------------
// You must not remove this notice, or any other, from this software.
// ----------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Responsiveness.Source.Extensions;
using Responsiveness.Source.Concurrency;

namespace Responsiveness
{
    struct Vector4
    {
        public double X;
        public double Y;
        public double Z;
        public double W;

        public static Vector4 operator+(Vector4 left, Vector4 right)
        {
            var result = left;
            result.X += right.X;
            result.Y += right.Y;
            result.Z += right.Z;
            result.W += right.W;
            return result;
        }

        public static Vector4 operator-(Vector4 left, Vector4 right)
        {
            var result = left;
            result.X -= right.X;
            result.Y -= right.Y;
            result.Z -= right.Z;
            result.W -= right.W;
            return result;
        }

        public static Vector4 operator*(Vector4 left, Vector4 right)
        {
            var result = left;
            result.X *= right.X;
            result.Y *= right.Y;
            result.Z *= right.Z;
            result.W *= right.W;
            return result;
        }

        public static Vector4 operator*(Vector4 left, double scale)
        {
            var result = left;
            result.X *= scale;
            result.Y *= scale;
            result.Z *= scale;
            result.W *= scale;
            return result;
        }

        public static Vector4 Create (double x, double y, double z, double w)
        {
            return new Vector4
            {
                X = x,
                Y = y,
                Z = z,
                W = w,
            };
        }

    }

    static class Extensions
    {
        public static Task HandleResult (this Task<bool> task)
        {
            return task.ContinueWith(t =>
                {
                    if (t.IsCanceled || !t.Result)
                    {
                       App.HandleFaults(new Exception ("Cancelled"));
                    }

                    if (t.IsFaulted)
                    {
                       App.HandleFaults(t.Exception);
                    }
                });
        }

        public static Task HandleFaults (this Task task)
        {
            return task.ContinueWith(t =>
                {
                   if (t.IsFaulted)
                   {
                       App.HandleFaults(t.Exception);
                   }
                });
        }

        public static void SetPixel (this byte[] pixels, int pos, Color c)
        {
            var i = pos * 4;
            pixels[i++] = c.B;
            pixels[i++] = c.G;
            pixels[i++] = c.R;
            pixels[i++] = 255;
        }

        public static Vector4 ToVector4 (this Color c)
        {
            const double multiplier = 1 / 255.0;
            return Vector4.Create(
                multiplier * c.A,
                multiplier * c.R,
                multiplier * c.G,
                multiplier * c.B
                );
        }

        public static Color ToColor (this Vector4 v)
        {
            const double multiplier = 255.0;
            return Color.FromArgb(
                (byte) Math.Round(multiplier * v.X),
                (byte) Math.Round(multiplier * v.Y),
                (byte) Math.Round(multiplier * v.Z),
                (byte) Math.Round(multiplier * v.W)
                );
        }

        public static Vector4 Lerp(this double t, Vector4 from, Vector4 to)
        {
            var diff = to - from;
            return from + diff*t;
        }

    }

    public partial class MainWindow
    {
        readonly static Color[] s_colors  =
        {
            Colors.Red          ,
            Colors.Yellow       ,
            Colors.Green        ,
            Colors.Cyan         ,
            Colors.Blue         ,
            Colors.Magenta      ,
            Colors.Red          ,
        };

        const int       MaxIter     = 2048                      ;
        const int       ImageWidth  = 2048                      ;
        const int       ImageHeight = 2048                      ;

        const double    CenterX     = 0.001643721971153         ;
        const double    CenterY     = 0.822467633298876         ;

        const double    ZoomX       = 0.00000000010             ;
        const double    ZoomY       = 0.00000000010             ;

        readonly Color[]                m_colorPalette          ;

        readonly WriteableBitmap m_bitmap = new WriteableBitmap (
            ImageWidth,
            ImageHeight,
            0,
            0,
            PixelFormats.Bgr32,
            null
            );

        readonly TaskScheduler sequentialTaskScheduler = new SequentialTaskScheduler ("SequentialTaskScheduler");
        readonly TaskScheduler defaultTaskScheduler = TaskScheduler.Default;

        CancellationTokenSource m_cancelSource;

        public MainWindow()
        {
            InitializeComponent();

            Img.Source = m_bitmap;

            var steps   = 16;

            var interpolatedColors = Enumerable
                .Range (0, s_colors.Length)
                .SelectMany (i =>
                {
                    var from    = s_colors[i % s_colors.Length].ToVector4();
                    var to      = s_colors[(i + 1) % s_colors.Length].ToVector4();
                    return Enumerable
                        .Range (0, steps)
                        .Select (ii => (((double)ii) / steps).Lerp(from, to).ToColor());
                })
                .ToArray ()
                ;

            m_colorPalette = new Color[MaxIter + 1];

            for (var iter = 0; iter < MaxIter; ++iter)
            {
                m_colorPalette[iter] = interpolatedColors[iter % interpolatedColors.Length];
            }

            m_colorPalette[MaxIter] = Colors.Black;
        }

        void Cancel ()
        {
            if (m_cancelSource != null)
            {
                m_cancelSource.Cancel();
                m_cancelSource.DisposeNoThrow();
                m_cancelSource = null;
            }
        }

        void CancelAndClear ()
        {
            Cancel ();

            m_cancelSource = new CancellationTokenSource();

            var pixels = new byte[m_bitmap.PixelHeight*4];

            for (var ix = 0; ix < m_bitmap.PixelWidth; ++ix)
            {
                m_bitmap.WritePixels(
                    new Int32Rect(ix, 0, 1, m_bitmap.PixelHeight),
                    pixels,
                    4,
                    0
                    );
            }
        }

        void Click_Cancel (object sender, EventArgs args)
        {
            Cancel ();
        }

        void Click_Render1 (object sender, EventArgs args)
        {
            CancelAndClear ();

            Dispatcher.InvokeAsync(() => RenderBitmap1 (m_cancelSource.Token));
        }

        void Click_Render4 (object sender, EventArgs args)
        {
            CancelAndClear ();

            ErrorPanel.Visibility = Visibility.Collapsed;

            Dispatcher.InvokeAsync(() => RenderBitmap4 (m_cancelSource.Token));
        }

        internal void DisplayErrorAsync(Exception exc)
        {
            Dispatcher.InvokeAsync(() => DisplayError (exc));
        }

        internal void DisplayError(Exception exc)
        {
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorMessage.Text = (exc ?? new Exception("Unknown error")).ToString ();
        }

        static void TraceThreadId()
        {
            Trace.WriteLine("ThreadId: {0}".FormatWith(Thread.CurrentThread.ManagedThreadId));
        }

        void AsyncWritePixels(byte[] pixels, int x, int y)
        {
            Dispatcher.InvokeAsync(() => WritePixels(pixels, x, y));
        }

        void WritePixels(byte[] pixels, int x, int y)
        {
            m_bitmap.WritePixels(
                new Int32Rect(x, y, 1, pixels.Length / 4),
                pixels,
                4,
                0
                );
        }


        int MandelBrot (double x, double y, int iter)
        {
            var ix = x;
            var iy = y;

            var i = 0;

            // Zn+1 = Zn^2 + C

            for (; (i < iter) & ((ix * ix + iy * iy) < 4); ++i)
            {
                var tx = ix * ix - iy * iy + x;
                iy = 2 * ix * iy + y;
                ix = tx;
            }

            return i;
        }

        bool RenderMandelbrot(
            CancellationToken ct,
            int x,
            int y,
            int width,
            int height,
            Action<byte[], int, int> columnWriter
            )
        {
            var tx = ZoomX/ImageWidth;
            var mx = CenterX - ZoomX / 2;
            var ty = ZoomY/ImageHeight;
            var my = CenterY - ZoomY / 2;

            for (var ix = 0; ix < width; ++ix)
            {
                var pixels = new byte[height*4];

                for (var iy = 0; iy < height; ++iy)
                {
                    var xx = tx * ix + mx;
                    var yy = ty * iy + my;
                    var iter = MandelBrot (xx,yy, MaxIter);
                    var color = m_colorPalette[iter];

                    pixels.SetPixel(iy, color);
                }

                if (ct.IsCancellationRequested)
                {
                    ix = width;
                }
                else
                {
                    columnWriter (pixels, x + ix, y);
                }

            }

            return !ct.IsCancellationRequested;
        }

        bool RenderMandelbrot(CancellationToken ct)
        {
            try
            {
                return RenderMandelbrot(
                    ct,
                    0,
                    0,
                    m_bitmap.PixelWidth,
                    m_bitmap.PixelHeight,
                    (pixels, x, y) => WritePixels(pixels, x, y));
            }
            catch (Exception exc)
            {
                DisplayErrorAsync (exc);
                return false;
            }
        }

        Task<bool> RenderMandelbrotAsync(CancellationToken ct)
        {
            var width = m_bitmap.PixelWidth;
            var height = m_bitmap.PixelHeight;

            return Task.Run(() => RenderMandelbrot(
                ct,
                0,
                0,
                width,
                height,
                AsyncWritePixels
                ),
                ct);
        }

    }
}

