#!/usr/bin/env python
# coding: utf-8

import time
import struct

import numpy as np
import tensorflow as tf
import cv2


# ## Object detection imports
# Here are the imports from the object detection module.
from utils import label_map_util
from utils import visualization_utils as vis_util


#trained models root
root = '..\\smd\\Singapore-Maritime-Dataset-Trained-Deep-Learning-Models-master\\trained_models'

# # Model preparation
MODEL_NAME = 'faster_rcnn_inception_v2_smd_2019_01_29'
#MODEL_NAME = 'ssd_mobilenet_v2_smd_2019_01_29'
#MODEL_NAME = 'ssd_inception_v2_smd_2019_01_29'

# Path to frozen detection graph. This is the actual model that is used for the object detection.
PATH_TO_FROZEN_GRAPH = root + '/' + MODEL_NAME + '/frozen_inference_graph.pb'

# List of the strings that is used to add correct label for each box.
PATH_TO_LABELS = root + '/' + MODEL_NAME + '/data/smd_label_map.pbtxt'

# ## Load a (frozen) Tensorflow model into memory.
detection_graph = tf.Graph()
with detection_graph.as_default():
  od_graph_def = tf.compat.v1.GraphDef()
  with tf.io.gfile.GFile(PATH_TO_FROZEN_GRAPH, 'rb') as fid:
    serialized_graph = fid.read()
    od_graph_def.ParseFromString(serialized_graph)
    tf.import_graph_def(od_graph_def, name='')

# ## Loading label map
# Label maps map indices to category names
category_index = label_map_util.create_category_index_from_labelmap(PATH_TO_LABELS, use_display_name=True)


def run_inference_for_single_image(image, graph):
    image_tensor = tf.compat.v1.get_default_graph().get_tensor_by_name('image_tensor:0')

    # Run inference
    output_dict = sess.run(tensor_dict, feed_dict={image_tensor: np.expand_dims(image, 0)})

    # all outputs are float32 numpy arrays, so convert types as appropriate
    output_dict['num_detections'] = int(output_dict['num_detections'][0])
    output_dict['detection_classes'] = output_dict['detection_classes'][0].astype(np.uint8)
    output_dict['detection_boxes'] = output_dict['detection_boxes'][0]
    output_dict['detection_scores'] = output_dict['detection_scores'][0]
    return output_dict


# Pipe to C# - Structures
MAX_DETECT_NUM = 300
out_scores = np.zeros(MAX_DETECT_NUM)
out_class = np.zeros(MAX_DETECT_NUM)
out_ymin = np.zeros(MAX_DETECT_NUM)
out_xmin = np.zeros(MAX_DETECT_NUM)
ouy_ymax = np.zeros(MAX_DETECT_NUM)
out_xmax = np.zeros(MAX_DETECT_NUM)

# Open Pipe to C#
f = open(r'\\.\pipe\DetectionData', 'r+b', 0)


# Input options
#     IP Camera
inputpath = "rtsp://192.168.10.14/bs1"
#     Ships Video
# Note:the next line should be uncommented if input path is from video file
# inputpath = "C:/projects/python/Singapore-Maritime-Dataset-Trained-Deep-Learning-Models-master/trained_models/faster_rcnn_inception_v2_smd_2019_01_29/test_vid/v6 a p.avi"


with detection_graph.as_default():
    with tf.compat.v1.Session() as sess:

            # Get handles to input and output tensors
            ops = tf.compat.v1.get_default_graph().get_operations()
            all_tensor_names = {output.name for op in ops for output in op.outputs}
            tensor_dict = {}
            for key in [
              'num_detections', 'detection_boxes', 'detection_scores',
              'detection_classes', 'detection_masks'
            ]:
                tensor_name = key + ':0'
                if tensor_name in all_tensor_names:
                    tensor_dict[key] = tf.compat.v1.get_default_graph().get_tensor_by_name(tensor_name)

            # Note: the line should be uncommented if input path is from video file
            # vid = cv2.VideoCapture(inputpath)

            i = 1
            while True:
                i += 1

                # Note:the next line should be commented if input path is from video file
                vid = cv2.VideoCapture(inputpath)

                (grabbed, frame) = vid.read()

                # Note:the next two lines should be commented if input path is from video file
                vid.release()
                time.sleep(3)

                if grabbed:

                    # Actual detection.
                    output_dict = run_inference_for_single_image(frame, detection_graph)

                    ## Visualization of the results of a detection.
                    # vis_util.visualize_boxes_and_labels_on_image_array(
                    #          frame,
                    #          output_dict['detection_boxes'],
                    #          output_dict['detection_classes'],
                    #          output_dict['detection_scores'],
                    #          category_index,
                    #          instance_masks=output_dict.get('detection_masks'),
                    #          use_normalized_coordinates=True,
                    #          line_thickness=4)

                    out_scores = np.array([s for s in output_dict['detection_scores']])
                    out_scores = np.pad(out_scores, (0,MAX_DETECT_NUM-len(out_scores)))
                    out_class = np.array([s for s in output_dict['detection_classes']])
                    out_class = np.pad(out_class, (0, MAX_DETECT_NUM-len(out_class)))
                    out_ymin = np.array([s[0] for s in output_dict['detection_boxes']])
                    out_ymin = np.pad(out_ymin, (0, MAX_DETECT_NUM-len(out_ymin)))
                    out_xmin = np.array([s[1] for s in output_dict['detection_boxes']])
                    out_xmin = np.pad(out_xmin, (0, MAX_DETECT_NUM-len(out_xmin)))
                    out_ymax = np.array([s[2] for s in output_dict['detection_boxes']])
                    out_ymax = np.pad(out_ymax, (0, MAX_DETECT_NUM-len(out_ymax)))
                    out_xmax = np.array([s[3] for s in output_dict['detection_boxes']])
                    out_xmax = np.pad(out_xmax, (0, MAX_DETECT_NUM-len(out_xmax)))

                    f.write(struct.pack("IIII", frame.shape[0], frame.shape[1], frame.shape[2], out_scores.shape[0]) +
                            bytes(frame) + bytes(out_scores) + bytes(out_class) + bytes(out_ymin) + bytes(out_xmin)+
                            bytes(out_ymax) + bytes(out_xmax))
                    f.seek(0)






