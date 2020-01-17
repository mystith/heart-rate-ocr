using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AForge.Imaging.Filters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using Tesseract;
using Rect = Tesseract.Rect;

namespace ocr
{
    class Program
    {
        public static Dictionary<string, Rate> cache;
        public static List<string> blacklist;
        public static StringBuilder sb;

        static void Main(string[] args)
        {
            try
            {
                blacklist = new List<string>();

                cache = new Dictionary<string, Rate>();

                sb = new StringBuilder();

                Thread thr = new Thread(() => {
                    while (true)
                    {
                        try
                        {
                            File.WriteAllText("OCROUTPUT.csv", sb.ToString());
                            Thread.Sleep(20000);
                        }catch(Exception e)
                        {
                            Console.WriteLine("Periodic write failed. {0}", e);
                            Thread.Sleep(20000);
                        }
                    }
                })
                {
                    IsBackground = true
                };
                thr.Start();

                sb.Append("Heart Rate,Video ID,Game");

                using (var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default))
                {
                    engine.DefaultPageSegMode = PageSegMode.SingleWord;
                    foreach (FileInfo fi in new DirectoryInfo(@"D:\New folder (5)\videos").GetFiles("*.png", SearchOption.AllDirectories))
                    {
                        try
                        {
                            Rate r = GetVideoInfo(fi.Directory.Name);
                            r.VideoID = fi.Directory.Name;

                            using (Image i = Image.FromFile(fi.FullName))
                            using (Bitmap og = new Bitmap(i, i.Width * 2, i.Height * 2))
                            using (Bitmap grays = SetGrayscale(og))
                            {
                                Median m = new Median(6);
                                using (Bitmap denoised = m.Apply(grays))
                                {
                                    using (var proc = engine.Process(denoised))
                                        r.HeartRate = proc.GetText();
                                }
                            }

                            string s = $"{r.HeartRate.Replace(" ", "").Replace("\n", "").Replace("\r", "")},{r.VideoID},{r.Game}";

                            sb.AppendLine(s);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Inner loop error: {0}", e);
                        }
                    }
                }

                File.WriteAllText("OCROUTPUT.csv", sb.ToString());

                Console.WriteLine("done");

                foreach (string s in blacklist)
                    Console.WriteLine(s);
                Console.ReadLine();
            }catch(Exception e)
            {
                File.WriteAllText("OCROUTPUT.csv", sb.ToString());
                Console.WriteLine("Outer error: {0}", e);
                Console.WriteLine("file written");
                Console.ReadLine();
            }
        }

        //Resize
        public static Bitmap Resize(Bitmap bmp, int newWidth, int newHeight)
        {
            Bitmap temp = (Bitmap)bmp;

            Bitmap bmap = new Bitmap(newWidth, newHeight, temp.PixelFormat);

            double nWidthFactor = (double)temp.Width / (double)newWidth;
            double nHeightFactor = (double)temp.Height / (double)newHeight;

            double fx, fy, nx, ny;
            int cx, cy, fr_x, fr_y;
            Color color1 = new Color();
            Color color2 = new Color();
            Color color3 = new Color();
            Color color4 = new Color();
            byte nRed, nGreen, nBlue;

            byte bp1, bp2;

            for (int x = 0; x < bmap.Width; ++x)
            {
                for (int y = 0; y < bmap.Height; ++y)
                {

                    fr_x = (int)Math.Floor(x * nWidthFactor);
                    fr_y = (int)Math.Floor(y * nHeightFactor);
                    cx = fr_x + 1;
                    if (cx >= temp.Width) cx = fr_x;
                    cy = fr_y + 1;
                    if (cy >= temp.Height) cy = fr_y;
                    fx = x * nWidthFactor - fr_x;
                    fy = y * nHeightFactor - fr_y;
                    nx = 1.0 - fx;
                    ny = 1.0 - fy;

                    color1 = temp.GetPixel(fr_x, fr_y);
                    color2 = temp.GetPixel(cx, fr_y);
                    color3 = temp.GetPixel(fr_x, cy);
                    color4 = temp.GetPixel(cx, cy);

                    // Blue
                    bp1 = (byte)(nx * color1.B + fx * color2.B);

                    bp2 = (byte)(nx * color3.B + fx * color4.B);

                    nBlue = (byte)(ny * (double)(bp1) + fy * (double)(bp2));

                    // Green
                    bp1 = (byte)(nx * color1.G + fx * color2.G);

                    bp2 = (byte)(nx * color3.G + fx * color4.G);

                    nGreen = (byte)(ny * (double)(bp1) + fy * (double)(bp2));

                    // Red
                    bp1 = (byte)(nx * color1.R + fx * color2.R);

                    bp2 = (byte)(nx * color3.R + fx * color4.R);

                    nRed = (byte)(ny * (double)(bp1) + fy * (double)(bp2));

                    bmap.SetPixel(x, y, System.Drawing.Color.FromArgb
            (255, nRed, nGreen, nBlue));
                }
            }



            bmap = SetGrayscale(bmap);
            bmap = RemoveNoise(bmap);

            return bmap;

        }


        //SetGrayscale
        public static Bitmap SetGrayscale(Bitmap img)
        {

            Bitmap temp = (Bitmap)img;
            Bitmap bmap = (Bitmap)temp.Clone();
            Color c;
            for (int i = 0; i < bmap.Width; i++)
            {
                for (int j = 0; j < bmap.Height; j++)
                {
                    c = bmap.GetPixel(i, j);
                    byte gray = (byte)(.299 * c.R + .587 * c.G + .114 * c.B);

                    bmap.SetPixel(i, j, Color.FromArgb(gray, gray, gray));
                }
            }
            return (Bitmap)bmap.Clone();

        }
        //RemoveNoise
        public static Bitmap RemoveNoise(Bitmap bmap)
        {

            for (var x = 0; x < bmap.Width; x++)
            {
                for (var y = 0; y < bmap.Height; y++)
                {
                    var pixel = bmap.GetPixel(x, y);
                    if (pixel.R < 162 && pixel.G < 162 && pixel.B < 162)
                        bmap.SetPixel(x, y, Color.Black);
                    else if (pixel.R > 162 && pixel.G > 162 && pixel.B > 162)
                        bmap.SetPixel(x, y, Color.White);
                }
            }

            return bmap;
        }

        public static Rate GetVideoInfo(string id)
        {
            if (blacklist.Contains(id)) return new Rate();

            if (cache.ContainsKey(id))
            {
                return cache[id];
            }

            try
            {
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create($"https://api.twitch.tv/kraken/videos/{ id }");

                rq.Headers.Add("Client-ID", "43we8ers8z2mjym0pjf0s9udwcoki7");

                using (WebResponse resp = rq.GetResponse())
                {
                    using (Stream s = resp.GetResponseStream())
                    {
                        using (StreamReader sr = new StreamReader(s))
                        {
                            JToken jt = JsonConvert.DeserializeObject<JToken>(sr.ReadToEnd());

                            Rate r = new Rate()
                            {
                                Game = jt["game"].Value<string>(),
                            };

                            cache.Add(r.VideoID, r);

                            return r;
                        }
                    }
                }
            }catch(Exception e)
            {
                Console.WriteLine(id);
                blacklist.Add(id);
                return new Rate();
            }
        }
    }
}
