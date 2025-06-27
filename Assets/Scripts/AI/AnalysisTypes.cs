/*************************************************************************
 *  Traversify – AnalysisTypes                                           *
 *  Author : OpenAI Assistance                                           *
 *  Desc   : Common DTOs & enums shared by MapAnalyzer, ModelGenerator,  *
 *           SegmentationVisualizer, etc.  Kept generic—no hard‑coded    *
 *           classes or labels so the system can self‑adapt.             *
 *************************************************************************/
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Traversify.AI
{
    /*──────────────────────── Enumerations ─────────────────────────────*/
    /// <summary>
    /// High‑level step identifiers for progress callbacks.
    /// </summary>
    public enum AnalysisStage
    {
        None             = 0,
        YoloDetection    = 1,
        Sam2Segmentation = 2,
        FasterRcnn       = 3,
        OpenAIEnhance    = 4,
        HeightEstimation = 5,
        Finalizing       = 6
    }

    /// <summary>
    /// Similarity metric used during clustering.
    /// </summary>
    public enum SimilarityMetric
    {
        Cosine,
        Euclidean,
        Manhattan
    }

    /*──────────────────── Data‑carrier classes (POCO) ──────────────────*/
    [Serializable]
    public class TerrainFeature
    {
        public string     type;           // e.g. "hill", "river"
        public string     label;          // human‑readable label
        public Rect       boundingBox;    // in source‑image pixels
        public Texture2D  segmentMask;    // alpha‑encoded mask
        public Color      segmentColor;   // overlay color
        public float      confidence;     // 0‑1
        public float      elevation;      // world‑space Y offset
    }

    [Serializable]
    public class MapObject
    {
        public string     type;               // e.g. "building"
        public string     label;              // raw class label
        public string     enhancedDescription;// OpenAI description
        public Vector2    position;           // normalized (0‑1, 0‑1)
        public Rect       boundingBox;        // pixels
        public Texture2D  segmentMask;
        public Color      segmentColor;
        public Vector3    scale;              // world localScale
        public float      rotation;           // Y‑axis
        public float      confidence;         // 0‑1
        public bool       isGrouped;          // part of cluster
    }

    [Serializable]
    public class ObjectGroup
    {
        public string           groupId;  // guid
        public string           type;     // cluster type name
        public List<MapObject>  objects   = new();
    }
}
