using System;
using NumSharp;

using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Tensorflow;
using static Tensorflow.Binding;
using Buffer = System.Buffer;

namespace TFDetect
{
    public class Results
    {
        public byte[] ClassesID;
        public string[] ClassesName;
        public float[] Scores;
        public float[] top;
        public float[] left;
        public float[] bottom;
        public float[] right;

        public Results(int count)
        {
            this.ClassesID = new byte[count];
            this.ClassesName = new string[count];
            this.Scores = new float[count];
            this.top = new float[count];
            this.left = new float[count];
            this.bottom = new float[count];
            this.right = new float[count];
        }
    }
    public static class TFDetect
    {
        private static float MIN_SCORE = 0.5f;
        private static string modelDir = "E:\\Download\\faster_rcnn_resnet50_smd_2019_01_29";
        private static string imageDir = "E:\\Download\\faster_rcnn_resnet50_smd_2019_01_29\\images";
        private static string pbFile = "frozen_inference_graph.pb";
        private static string pbxFile = "label_map.pbtxt";
        private static Graph graph;
        private static PbtxtItems labels;

        public static bool Init(string ModelFile, string LabelFile)
        {

            bool bRes = false;


            try
            {
                tf.compat.v1.disable_eager_execution();
                graph = ImportGraph();
                labels = PbtxtParser.ParsePbtxtFileSMD(Path.Combine(modelDir, pbxFile));
                bRes = true;
            }
            catch (Exception e)
            {

            }

            return bRes;
        }



        private static Graph ImportGraph()
        {
            Graph _graph = new Graph().as_default();
            _graph.Import(Path.Combine(modelDir, pbFile));
            return _graph;
        }

        public static void Predict(Bitmap bmp, ref Results results)
        {
            // read in the input image

            NDArray imgArr = bmp.ToNDArray(discardAlpha: true);
            imgArr.shape = new int[] { 1, bmp.Height, bmp.Width, 3 };


            using (Session sess = tf.Session(graph))
            {
                Tensor tensorNum = graph.OperationByName("num_detections");
                Tensor tensorBoxes = graph.OperationByName("detection_boxes");
                Tensor tensorScores = graph.OperationByName("detection_scores");
                Tensor tensorClasses = graph.OperationByName("detection_classes");
                Tensor imgTensor = graph.OperationByName("image_tensor");
                Tensor[] outTensorArr = new Tensor[] { tensorNum, tensorBoxes, tensorScores, tensorClasses };

                NDArray[] ndResults = sess.run(outTensorArr, new FeedItem(imgTensor, imgArr));

                results=BuildOutputData(ndResults, bmp.Size);
            }
        }

        /*
        private static NDArray ReadTensorFromImageFile(Bitmap bmp)
        {
            var graph = tf.Graph().as_default();

            string file_name = @"E:\Download\faster_rcnn_resnet50_smd_2019_01_29\images\ships1.jpg";
            Tensor file_reader = tf.io.read_file(file_name, "file_reader");
            Tensor decodeFromFile = tf.image.decode_jpeg(file_reader, channels: 3, name: "DecodeJpeg");

            NDArray nd = bmp.ToNDArray(discardAlpha: true);
            nd.shape = new int[]{ 1, bmp.Height, bmp.Width, 3 };
            Tensor content = new Tensor(nd,TF_DataType.TF_UINT8);
            return nd;
            var decodeJpeg = tf.image.decode_image(content);
            
            var casted = tf.cast(decodeJpeg, TF_DataType.TF_UINT8);
            var dims_expander = tf.expand_dims(casted, 0);

            using (var sess = tf.Session(graph))
                return sess.run(dims_expander);
        }
        */

        private static NDArray GetBitmapBytes(Bitmap image, out byte[] RawBytes)
        {
            // En garde!
            if (image == null) throw new ArgumentNullException(nameof(image));

            BitmapData bmpData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, image.PixelFormat);
            try
            {
                unsafe
                {
                    RawBytes = new byte[bmpData.Stride * image.Height];
                    //Create a 1d vector without filling it's values to zero (similar to np.empty)
                    var nd = new NDArray(NPTypeCode.Byte, Shape.Vector(bmpData.Stride * image.Height), fillZeros: false);

                    // Get the respective addresses
                    byte* src = (byte*)bmpData.Scan0;
                    byte* dst = (byte*)nd.Unsafe.Address; //we can use unsafe because we just allocated that array and we know for sure it is contagious.

                    Marshal.Copy(bmpData.Scan0, RawBytes, 0, RawBytes.Length);
                    // Copy the RGB values into the array.
                    Buffer.MemoryCopy(src, dst, nd.size, nd.size); //faster than Marshal.Copy
                    //TODO: replace return with: return nd.reshape(batch_size, height, width, 3);
                    return nd;
                }
            }
            finally
            {
                image.UnlockBits(bmpData);
            }
        }

        private static Results BuildOutputData(NDArray[] resultArr, Size imgSize)
        {
            // get pbtxt items 
            //pbxFile = "mscoco_label_map.pbtxt";

            Results res = new Results((int)resultArr[0].GetAtIndex<float>(0));

            var scores = resultArr[2].AsIterator<float>();
            var boxes = resultArr[1].GetData<float>();
            var id = np.squeeze(resultArr[3]).GetData<float>();

            res.Scores = scores.ToArray();
            for (int i = 0; i < id.Count; i++)
            {
                res.ClassesID[i] = (byte)(id[i]-1);
                res.ClassesName[i] = labels.items[res.ClassesID[i]].display_name;
                res.left[i] = boxes[i * 4 + 1];
                res.right[i] = boxes[i * 4 + 3];
                res.top[i] = boxes[i * 4];
                res.bottom[i] = boxes[i * 4 + 2];
            }


            /*
            for (int i = 0; i < scores.size; i++)
            {
                float score = scores.MoveNext();
                float top = boxes[i * 4] * imgSize.Height;
                float left = boxes[i * 4 + 1] * imgSize.Width;
                float bottom = boxes[i * 4 + 2] * imgSize.Height;
                float right = boxes[i * 4 + 3] * imgSize.Width;
                
                string name = labels.items.Where(w => w.id == id[i]).Select(s => s.display_name).FirstOrDefault();

                //drawObjectOnBitmap(newBitmap, rect, score, name);

            }
            */
            return res;
        }

        private static void drawObjectOnBitmap(Bitmap bmp, Rectangle rect, float score, string name)
        {
            using (Graphics graphic = Graphics.FromImage(bmp))
            {
                graphic.SmoothingMode = SmoothingMode.AntiAlias;

                using (Pen pen = new Pen(Color.Red, 2))
                {
                    graphic.DrawRectangle(pen, rect);

                    Point p = new Point(rect.Right + 5, rect.Top + 5);
                    string text = string.Format("{0}:{1}%", name, (int)(score * 100));
                    graphic.DrawString(text, new Font("Verdana", 8), Brushes.Red, p);
                }
            }
        }
    }

    public class PbtxtItem
    {
        public string name { get; set; }
        public int id { get; set; }
        public string display_name { get; set; }
    }
    public class PbtxtItems
    {
        public List<PbtxtItem> items { get; set; }
    }
    public class PbtxtParser
    {
        public static PbtxtItems ParsePbtxtFileSMD(string filePath)
        {
            PbtxtItems items;

            using (System.IO.StreamReader reader = new System.IO.StreamReader(filePath))
            {

                string s = reader.ReadToEnd();
                items = JsonConvert.DeserializeObject<PbtxtItems>(s);



            }
            return items;
        }

        public static PbtxtItems ParsePbtxtFile(string filePath)
        {
            string line;
            PbtxtItems items;
            string newText = "{\"items\":[";

            using (System.IO.StreamReader reader = new System.IO.StreamReader(filePath))
            {


                while ((line = reader.ReadLine()) != null)
                {
                    string newline = string.Empty;

                    if (line.Contains("{"))
                    {
                        newline = line.Replace("item", "").Trim();
                        //newText += line.Insert(line.IndexOf("=") + 1, "\"") + "\",";
                        newText += newline;
                    }
                    else if (line.Contains("}"))
                    {
                        newText = newText.Remove(newText.Length - 1);
                        newText += line;
                        newText += ",";
                    }
                    else
                    {
                        newline = line.Replace(":", "\":").Trim();
                        newline = "\"" + newline;// newline.Insert(0, "\"");
                        newline += ",";

                        newText += newline;
                    }

                }

                newText = newText.Remove(newText.Length - 1);
                newText += "]}";

                reader.Close();
            }

            items = JsonConvert.DeserializeObject<PbtxtItems>(newText);

            return items;
        }
    }

    public static class Test
    {
        public static void TestMethod()
        {
            Bitmap bmp = new Bitmap(@"E:\Download\faster_rcnn_resnet50_smd_2019_01_29\images\ships1.jpg");

            string model = "E:\\Download\\faster_rcnn_resnet50_smd_2019_01_29\\frozen_inference_graph.pb";
            string label = "E:\\Download\\faster_rcnn_resnet50_smd_2019_01_29\\label_map.pbtxt";
            
            Results res=null;

            TFDetect.Init(model, label);
            TFDetect.Predict(bmp, ref res);


        }
    }
}
