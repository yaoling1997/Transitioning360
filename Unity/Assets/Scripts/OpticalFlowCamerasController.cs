using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Runtime.InteropServices;

public class OpticalFlowCamerasController : MonoBehaviour
{
    private static readonly float oo = 1e9f;
    public List<float[,]> pixelSaliency;//Saliency for each pixel
    public List<float[,]> normalizedPixelSaliency;//Normalized pixel of each saliency
    //public List<float[,]> downsamplePixelSaliency;//Saliency of each pixel after downsample
    public List<float[,]> regionalSaliency;
    public List<Vector2[,]> pixelOpticalFlow;
    public List<float[,]> pixelMaskList;
    public Rect[,] rectOfPixel;//Each pixel is the approximate field of view rectangle corresponding to the center (the upper left corner is the smallest vertex, and the lower right corner is the largest vertex)

    public List<Vector2>[] initialPath;
    public List<Vector2>[] fovAwarePath;
    public List<Vector2>[] smoothPath;

    //To save unnecessary loading time, the following two need to be recorded in the testData when loading data. The user can restore directly from testData after switching scenes.
    public List<float[,]> downsamplePixelBlend;
    public List<float[,]> downsampleRegionalSaliency;
    public List<Vector2[,]> downsamplePixelOpticalFlow;


    //Whether to use GPU to speed up the operation
    public bool useGpu;

    //Visualize some parameters for debugging
    public Image testImage;

    private Manager manager;
    private PanelVideoController panelVideoController;
    private MenuBarController menuBarController;    
    private VideoPlayer videoPlayer;
    
    public List<List<float[,]>> everyCameraDownsamplePixelSaliency;
    public List<List<float[,]>> everyCameraDownsampleRegionalSaliency;

    private ComputeShader initialPathPlanningComputeShader;
    public ComputeShader initialPathPlanningMethod3ComputeShader;
    private int kernel;//kernel号
    //Buffer that transmits data to ComputeShader
    private ComputeBuffer preFComputeBuffer;
    private ComputeBuffer downsamplePixelSaliencyComputeBuffer;
    private ComputeBuffer downsamplePixelOpticalFlowComputeBuffer;
    private ComputeBuffer preCameraPreKeyFramePosComputeBuffer;
    private ComputeBuffer preCameraNowKeyFramePosComputeBuffer;
    private ComputeBuffer nowFComputeBuffer;
    private ComputeBuffer nowPComputeBuffer;

    private ComputeBuffer singleRectAreaComputeBuffer;
    private ComputeBuffer twoRectOverlapAreaComputeBuffer;        
    private ComputeBuffer salEResultComputeBuffer;

    private float[] nowFDataReceiver;
    private int[] nowPDataReceiver;
    
    private float[] salEResultDataReceiver;

    public RawImage method3PixelSaliencyContent;
    public RawImage method3OpticalFlowContent;
    public RawImage method3RestValuablePixelContent;
    public List<Texture2D> method3OpticalFlowTextureList;
    public List<Texture2D> method3PixelSaliencyTextureList;
    public List<Texture2D> method3RestValuablePixelTextureList;

    public string resultDir;

    //Remember to delete the variables used for debugging
    public float pixelSaliencyThreshold;
    public float decayTheta;
    public float filterTheta;
    public int gaussianKernelSize;
    public bool ifUseGaussianKernelToSmoothSaliency;
    public float wp;
    public float w1;
    public float preCamPosTheta;
    public float preCamVelTheta;
    public bool ifAddCamSimilarity;
    //Whether to use all key frame maxima to normalize
    public bool ifUseAllFramePixelSaliencyMaxNormalize;
    public float overlapWeight;
    public int updateRadius;
    public float greedyW0;
    public float greedyIouThreshold;
    public float pixelSaliencyEnhanceWeight;
    public float w0;
    public float wop;
    public float method3RefineW0;
    public bool ifRoundOpticalFlow;
    public bool ifRefinePathMethod3;
    public bool ifUseNewRefineMethod3;
    public bool ifUseMaxBlendMethod3;
    public bool ifUseTopKOpticalFlowMethod3;//top k optical flow
    public bool ifUseTopKOpticalFlowMaxSalMethod3;//The pixels of the front K largest optical flow point to the same position as the largest saliency after polling, as the coarse lattice saliency.
    public int k_Method3;

    public bool ifUseNormalizationPixelSaliency;

    public int restMinimalXDist;
    public int restMinimalYDist;
    public float restMinimalPS;
    public float restMinimalOF;
    public int restMinimalPSNum;
    public int beginKeyFrameOfGetRestValuablePixelNum;

    public bool ifUseFovPath;
    public bool ifAutoDetermineCamNum;

    public float blendSaliencyWeight;
    public float blendMaskWeight;

    public bool useMaskDecay;//Use the mask decay effect from top to bottom to make the mask value of the lower body smaller
    public float maskDecayV;//Decay number from top to bottom of the same person

    public bool ifInitPathUseGreedy;//Using greedy to calculate the initial path
    public bool withoutOverlapItem;

    public float preparePathTotalTime;//Time of path from nothing to existence
    public float initPathTotalTime;//Time spent on initial path DP
    public float initPathJointDpTime;//Initial path joint DP time
    public float initPathRefineTime;//DP finish refine time
    public float renderCamWindowTime;//Time to render the camera frame
    public float smoothPathTime;//Smoothing the path takes time

    private void Awake()
    {        
        resultDir = System.Environment.CurrentDirectory + "\\" + "pathResult";
        pixelSaliencyThreshold = 0.3f;
        decayTheta = 15;
        filterTheta = 10;        
        gaussianKernelSize = 7;
        ifUseGaussianKernelToSmoothSaliency = true;        
        wp = 1e-4f;
        w1 = 0.1f;
        wop = 0.1f;
        preCamPosTheta = 10;
        preCamVelTheta = 10;
        ifAddCamSimilarity = false;
        ifUseAllFramePixelSaliencyMaxNormalize = false;
        overlapWeight = 1.5f;
        updateRadius = 21;
        greedyW0 = 0.03f;
        greedyIouThreshold = 0.2f;
        pixelSaliencyEnhanceWeight = 1;
        w0 = 1f;        
        method3RefineW0 = 0.1f;        
        ifRoundOpticalFlow = true;
        ifRefinePathMethod3 = true;
        ifUseNewRefineMethod3 = false;
        ifUseMaxBlendMethod3 = false;
        ifUseTopKOpticalFlowMethod3 = true;
        ifUseTopKOpticalFlowMaxSalMethod3 = true;
        k_Method3 = 10;
        ifUseNormalizationPixelSaliency = false;

        ifInitPathUseGreedy = false;
        withoutOverlapItem = false;

        restMinimalXDist = 15;
        restMinimalYDist = 15;
        restMinimalPS = 0.02f;
        restMinimalOF = 0f;
        restMinimalPSNum = 20;
        beginKeyFrameOfGetRestValuablePixelNum = 5;
        ifUseFovPath = false;
        ifAutoDetermineCamNum = false;

        blendSaliencyWeight = 1;
        blendMaskWeight = 0;

        useMaskDecay = false;
        maskDecayV = 0.01f;

        manager = GameObject.Find("Manager").GetComponent<Manager>();
        panelVideoController = manager.panelVideoController;        
        initialPathPlanningComputeShader = manager.initialPathPlanningComputeShader;
        initialPathPlanningMethod3ComputeShader = manager.initialPathPlanningMethod3ComputeShader;
        videoPlayer = manager.videoPlayer;
        useGpu = true;
    }

    // Use this for initialization
    void Start()
    {                        
    }

    // Update is called once per frame
    void Update ()
    {
        //initial,fov,smooth...    
        var cameraNFOVs = panelVideoController.cameraNFOVs;
        if (cameraNFOVs != null && videoPlayer.isPlaying) {
            var sceneNumber = Manager.GetActivateSceneNumber();
            if (sceneNumber == 0)
            {
                UpdateCameras();
                UpdateMehtod3OpticalFlow();
                UpdateMehtod3PixelSaliency();
                UpdateMehtod3RestValuablePixel();
            }
            else if (sceneNumber == 1)
            {
                var camGroupNum = panelVideoController.cameraGroupNum;
                for (int i = 0; i < camGroupNum; i++)
                {
                    if (smoothPath != null)
                        UpdateCamera(smoothPath[i], cameraNFOVs[i]);
                }
                manager.mainNFOVController.UpdateMainNFOVCamera();
            }
            else if (sceneNumber >= 2) {
                var camGroupNum = panelVideoController.cameraGroupNum;
                for (int i = 0; i < camGroupNum; i++)
                {
                    if (smoothPath != null && cameraNFOVs != null)
                        UpdateCamera(smoothPath[i], cameraNFOVs[i]);
                }
            }
        }
    }
    
    public void UserStudySceneUpdateCameras() {
        var cameraNFOVs = panelVideoController.cameraNFOVs;
        var camGroupNum = panelVideoController.cameraGroupNum;
        for (int i = 0; i < camGroupNum; i++)
        {
            if (smoothPath != null && cameraNFOVs != null)
                UpdateCamera(smoothPath[i], cameraNFOVs[i]);
        }
    }
    
    public void UpdateMehtod3OpticalFlow() {
        if (method3OpticalFlowTextureList == null && method3OpticalFlowTextureList.Count == 0)
            return;
        var keyFrame = (int)((float)videoPlayer.frame / videoPlayer.frameCount * method3OpticalFlowTextureList.Count);
        if (keyFrame < method3OpticalFlowTextureList.Count && videoPlayer.isPlaying)
        {
            var t = method3OpticalFlowTextureList[keyFrame];
            method3OpticalFlowContent.texture = t;
        }
    }
    public void UpdateMehtod3PixelSaliency()
    {
        if (method3PixelSaliencyTextureList == null && method3PixelSaliencyTextureList.Count == 0)
            return;
        var keyFrame = (int)((float)videoPlayer.frame / videoPlayer.frameCount * method3PixelSaliencyTextureList.Count);
        if (keyFrame < method3PixelSaliencyTextureList.Count && videoPlayer.isPlaying)
        {
            var t = method3PixelSaliencyTextureList[keyFrame];
            method3PixelSaliencyContent.texture = t;
        }
    }
    public void UpdateMehtod3RestValuablePixel()
    {
        if (method3RestValuablePixelTextureList == null && method3RestValuablePixelTextureList.Count == 0)
            return;
        var keyFrame = (int)((float)videoPlayer.frame / videoPlayer.frameCount * method3RestValuablePixelTextureList.Count);
        if (keyFrame < method3RestValuablePixelTextureList.Count && videoPlayer.isPlaying)
        {
            var t = method3RestValuablePixelTextureList[keyFrame];
            method3RestValuablePixelContent.texture = t;
        }
    }
    
    public void UpdateCameras() {
        var cameraNFOVs = panelVideoController.cameraNFOVs;
        var camGroupSize = panelVideoController.cameraGroupSize;
        for (int i = 0; i < cameraNFOVs.Length; i += camGroupSize)
        {
            if (initialPath != null)
                UpdateCamera(initialPath[i / camGroupSize], cameraNFOVs[i]);
            if (fovAwarePath != null)
                UpdateCamera(fovAwarePath[i / camGroupSize], cameraNFOVs[i + 1]);
            if (smoothPath != null)
                UpdateCamera(smoothPath[i / camGroupSize], cameraNFOVs[i + 2]);
        }
    }
    
    public void UpdateCamera(List<Vector2> chosenPath, Camera chosenCamera)
    {
        if (chosenPath == null || chosenPath.Count == 0 || chosenCamera == null)
            return;
        RectangleInterpolation(chosenPath, chosenCamera);
    }
    
    public void RectangleInterpolation(List<Vector2> chosenPath, Camera chosenCamera)
    {
        var currentFrame = Mathf.Min(videoPlayer.frame, videoPlayer.frameCount - 1);
        var totalKeyFrame = chosenPath.Count;
        var percentage = (float)currentFrame / videoPlayer.frameCount;
        int l = (int)((float)currentFrame * totalKeyFrame / videoPlayer.frameCount);
        int r = l + 1;
        var originSize = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var al = chosenPath[l];
        var tl = (float)l / chosenPath.Count;
        if (r == chosenPath.Count)
        {
            chosenCamera.transform.forward = panelVideoController.PixelToVector3(PixelToOriginPixelIntValue(chosenPath[l], new Vector2(downsampleWidth, downsampleHeight), originSize));
        }
        else
        {            
            var ar = chosenPath[r];
            var tr = (float)r / chosenPath.Count;
            var v = GetVector2Of2Pixels(al, ar, downsampleWidth);            
            var tmp = (percentage - tl) / (tr - tl);
            tmp = Mathf.Clamp(tmp, 0, 1);
            v = v * tmp;
            var p = al + v;
            NormalizePixelInRange(ref p.x, ref p.y, downsampleWidth, downsampleHeight);
            var op = PixelToOriginPixelFloatValue(p, new Vector2(downsampleWidth, downsampleHeight), originSize);            
            chosenCamera.transform.forward = panelVideoController.PixelToVector3(op);
        }
    }
    public void SphereInterpolation(List<Vector2> chosenPath, Camera chosenCamera) {
        var currentFrame = Mathf.Min(videoPlayer.frame, videoPlayer.frameCount - 1);
        var totalKeyFrame = chosenPath.Count;
        var percentage = (float)currentFrame / videoPlayer.frameCount;
        int l = (int)((float)currentFrame * totalKeyFrame / videoPlayer.frameCount);
        int r = l + 1;
        var originSize = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var al = panelVideoController.PixelToAngle(PixelToOriginPixelIntValue(chosenPath[l], new Vector2(downsampleWidth, downsampleHeight), originSize));
        var tl = (float)l / chosenPath.Count;
        if (r == chosenPath.Count)//Only the view of the last camera is left
        {
            chosenCamera.transform.eulerAngles = panelVideoController.AngleToEulerAngle(al);
        }
        else
        {            
            var ar = panelVideoController.PixelToAngle(PixelToOriginPixelIntValue(chosenPath[r], new Vector2(downsampleWidth, downsampleHeight), originSize));
            var tr = (float)r / chosenPath.Count;
            var u = panelVideoController.EulerAngleToVector3(panelVideoController.AngleToEulerAngle(al));
            var v = panelVideoController.EulerAngleToVector3(panelVideoController.AngleToEulerAngle(ar));
            var theta = Vector3.Angle(u, v) * (percentage - tl) / (tr - tl) * Mathf.Deg2Rad;
            var w = Vector3.RotateTowards(u, v, theta, 1);
            chosenCamera.transform.forward = w;
        }
    }
    
    private void FindPixelInFOV(int x, int y, int width, int height, Vector2 camAngle, int originWidth, int originHeight, int timeStep, int[,] vis, List<Vector2> pixelsInFOV, Vector2[] moveWays, ref int xMin, ref int xMax, ref int yMin, ref int yMax)
    {
        var xx = (x + width) % width;
        var yy = (y + height) % height;
        if (xx < 0 || x >= 2 * width)
            return;
        //No cross boundary allowed in vertical direction
        if (y < 0 || y >= height)
            return;

        var originXx = (float)xx / width * originWidth;
        var originYy = (float)yy / height * originHeight;
        var originP = new Vector2(originXx, originYy);
        if (!panelVideoController.IfInFOV(camAngle, panelVideoController.PixelToAngle(originP)))
            return;        

        if (vis[xx, yy] == timeStep)
            return;
        vis[xx, yy] = timeStep;
        pixelsInFOV.Add(new Vector2(xx, yy));
        xMin = Mathf.Min(xMin, x);
        yMin = Mathf.Min(yMin, y);
        xMax = Mathf.Max(xMax, x);
        yMax = Mathf.Max(yMax, y);
        foreach (var m in moveWays)
        {
            FindPixelInFOV(x + (int)m.x, y + (int)m.y, width, height, camAngle, originWidth, originHeight, timeStep, vis, pixelsInFOV, moveWays, ref xMin, ref xMax, ref yMin, ref yMax);
        }
    }
    //Zooms pixels to the pixel coordinates of the original video
    public Vector2 PixelToOriginPixelIntValue(Vector2 p, Vector2 size, Vector2 originSize)
    {
        p = new Vector2((int)p.x, (int)p.y);
        var wSeg = originSize.x / size.x;
        var hSeg = originSize.y / size.y;
        return new Vector2((int)(p.x / size.x * originSize.x + wSeg / 2), (int)(p.y / size.y * originSize.y + hSeg / 2));
    }

    //Zooms pixels to the pixel coordinates of the original video
    public Vector2 PixelToOriginPixelFloatValue(Vector2 p, Vector2 size, Vector2 originSize)
    {
        p = new Vector2(p.x, p.y);
        return new Vector2(p.x / size.x * originSize.x, p.y / size.y * originSize.y);
    }
    //Get the percentage of pixels contained
    private bool IfContainPixelPercentageOk(List<Vector2> xRangeList, Vector2 yRange, List<Vector2> pixelsInFOV, float neededPixelPercentage)
    {
        float cnt = 0;
        var neededPixelNum = neededPixelPercentage * pixelsInFOV.Count;
        for (int i = 0; i < pixelsInFOV.Count; i++)
        {
            var p = pixelsInFOV[i];
            foreach (var xRange in xRangeList)
                if (xRange.x <= p.x && p.x <= xRange.y)
                    if (yRange.x <= p.y && p.y <= yRange.y)
                    {
                        cnt++;
                        break;
                    }
            //prune
            if (cnt + pixelsInFOV.Count - i - 1 < neededPixelNum)
                return false;
        }
        return cnt >= neededPixelNum;
    }

    //Display graphics on testimage
    private void UpdateTestImage(int width, int height, List<Vector2> pixelsInFOV, Rect rectOfFOV)
    {
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var t = new Texture2D(width, height);

        for (int i = 0; i < width; i++)
            for (int j = 0; j < height; j++)
            {
                t.SetPixel(i, j, Color.white);
            }

        //Green indicates the field of vision
        foreach (var p in pixelsInFOV)
        {
            int xx = (int)p.x;
            int yy = (int)p.y;
            NormalizePixelInRange(ref xx, ref yy, downsampleWidth, downsampleHeight);
            t.SetPixel(xx, height - 1 - yy, Color.green);
        }

        //Red means rectangle
        for (int i = (int)rectOfFOV.xMin; i <= rectOfFOV.xMax; i++)
        {
            int xx = i;
            int yy = (int)rectOfFOV.yMin;
            NormalizePixelInRange(ref xx, ref yy, downsampleWidth, downsampleHeight);
            t.SetPixel(xx, height - 1 - yy, Color.red);
            yy = (int)rectOfFOV.yMax;
            NormalizePixelInRange(ref xx, ref yy, downsampleWidth, downsampleHeight);
            t.SetPixel(xx, height - 1 - yy, Color.red);
        }
        for (int i = (int)rectOfFOV.yMin; i <= rectOfFOV.yMax; i++)
        {
            int yy = i;
            int xx = (int)rectOfFOV.xMin;
            NormalizePixelInRange(ref xx, ref yy, downsampleWidth, downsampleHeight);
            t.SetPixel(xx, height - 1 - yy, Color.red);
            xx = (int)rectOfFOV.xMax;
            NormalizePixelInRange(ref xx, ref yy, downsampleWidth, downsampleHeight);
            t.SetPixel(xx, height - 1 - yy, Color.red);
        }
        t.Apply();
        testImage.sprite = Sprite.Create(t, new Rect(0, 0, width, height), new Vector2());
    }

    //Get the approximate field of view rectangle and the pixels in the field of view
    private void GetRectOfFOV(int x, int y, int width, int height, int originalWidth, int originalHeight, int timeStep, int[,] vis, out Rect rectOfFOV)
    {//Enumerated pixel coordinates (width, height), width and height of downsample graph, width and height of original video, timestamp for updating, two-dimensional array recording access status, rectangle approximate to irregular field of view, and pixels in irregular field of view
        //Get the pixel in the field of view
        var pixelsInFOV = new List<Vector2>();
        //Find how pixels move
        var moveWays = new Vector2[4] {
            new Vector2(-1,0),
            new Vector2(1,0),
            new Vector2(0,-1),
            new Vector2(0,1)
        };
        int xMin, xMax, yMin, yMax;
        xMin = yMin = (int)1e9;
        xMax = yMax = -(int)1e9;
        var camAngle = panelVideoController.PixelToAngle(PixelToOriginPixelFloatValue(new Vector2(x, y), new Vector2(width, height), new Vector2(originalWidth, originalHeight)));
        FindPixelInFOV(x, y, width, height, camAngle, originalWidth, originalHeight, timeStep, vis, pixelsInFOV, moveWays, ref xMin, ref xMax, ref yMin, ref yMax);

        if (xMax - xMin + 1 >= width)
        {
            xMin = x - width / 2;
            xMax = x + width / 2 - 1;
        }

        y = (yMin + yMax) / 2;
        var containPixelPercentage = 0.95f;//What percentage of pixels in the field of view should the approximate rectangle contain
        int w = 3;
        int h = 3;
        var xRangeList = RangeX(x - w / 2, x + w / 2, width);
        var yRange = RangeY(y - h / 2, y + h / 2, height);

        for (; ; )
        {
            if (h < yMax - yMin + 1)
                h += 2;
            yRange = RangeY(y - h / 2, y + h / 2, height);
            if (IfContainPixelPercentageOk(xRangeList, yRange, pixelsInFOV, containPixelPercentage))
                break;
            w += 2;
            xRangeList = RangeX(x - w / 2, x + w / 2, width);
            if (IfContainPixelPercentageOk(xRangeList, yRange, pixelsInFOV, containPixelPercentage))
                break;
        }
        w = Mathf.Min(w, width);
        rectOfFOV = new Rect(x - w / 2, y - h / 2, w, h);       
    }
    //The interval of Y corresponding to [0, H) is obtained
    private Vector2 RangeY(float l, float r, float h)
    {
        l = Mathf.Max(l, 0);
        r = Mathf.Min(r, h - 1);
        return new Vector2(l, r);
    }
    private List<Vector2> RangeX(float l, float r, float w)
    {
        var re = new List<Vector2>();
        if (r - l + 1 >= w)
        {
            re.Add(new Vector2(0, w - 1));
        }
        else if (l < 0)
        {
            re.Add(new Vector2(0, r));
            re.Add(new Vector2(l + w, w - 1));
        }
        else if (r >= w)
        {
            re.Add(new Vector2(l, w - 1));
            re.Add(new Vector2(0, r - w));
        }
        else
        {
            re.Add(new Vector2(l, r));
        }
        return re;
    }

    
    public bool IfPixelInRect(Vector2 p, Rect r,int w,int h) {
        var rangeY = RangeY(r.yMin, r.yMax, h);
        var rangeXList = RangeX(r.xMin, r.xMax, w);
        foreach (var rangeX in rangeXList)
            if (rangeX.x <= p.x && p.x <= rangeX.y)
                if (rangeY.x <= p.y && p.y <= rangeY.y)
                    return true;
        return false;
    }
    //Gets the value of the two-dimensional prefix sum
    private float Get2dPrefixSumValue(int x, int y, float[,] prefixSum)
    {
        if (x < 0 || y < 0)
            return 0;
        return prefixSum[x, y];
    }
    
    public void GetDownsampleSize(out int width,out int height) {
        var originWidth = videoPlayer.targetTexture.width;
        var originHeight = videoPlayer.targetTexture.height;
        width = 180;//The width of the down sample
        height = (int)((float)originHeight / originWidth * width);
    }

    //Initialize the downsamplewidth and downsampleheight to get the rectangle for each pixel
    public void GetRectsOfFOV() {
        
        var originWidth = videoPlayer.targetTexture.width;
        var originHeight = videoPlayer.targetTexture.height;
        int nowDownsampleWidth, nowDownsampleHeight;
        GetDownsampleSize(out nowDownsampleWidth, out nowDownsampleHeight);

        if (rectOfPixel != null)
            return;

        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        downsampleWidth = nowDownsampleWidth;
        downsampleHeight = nowDownsampleHeight;

        //Rectangle for each pixel
        rectOfPixel = new Rect[downsampleWidth, downsampleHeight];
        var timeStep = 0;
        Debug.Log(string.Format("downsampleWidth:{0}, downsampleHeight:{1}", downsampleWidth, downsampleHeight));
        var vis = new int[downsampleWidth, downsampleHeight];
        var time1 = DateTime.Now.Ticks;
        for (int j = 0; j < downsampleHeight; j++)
            for (int i = 0; i < downsampleWidth; i++)
            {
                timeStep++;
                GetRectOfFOV(i, j, downsampleWidth, downsampleHeight, originWidth, originHeight, timeStep, vis, out rectOfPixel[i, j]);                
            }        
        var time2 = DateTime.Now.Ticks;
        Debug.Log("get all rects time: "+(time2 - time1) / 10000000);        
    }
    
    public static int GetRectWidth(Rect r) {
        return (int)r.width + 1;
    }

    public static int GetRectHeight(Rect r)
    {
        return (int)r.height + 1;
    }
    
    public void PrepareRegionalSaliency() {
        var ps = pixelSaliency;
        if (ifUseNormalizationPixelSaliency)
            ps = normalizedPixelSaliency;
        List<float> noThing = null;
        regionalSaliency = PreprocessRegionalSaliency(ps, MenuBarController.pixelSaliencyWidth, MenuBarController.pixelSaliencyHeight,ref noThing, false);
    }    
    public List<float[,]> PreprocessRegionalSaliency(List<float[,]> pixelSaliency,int pixelSaliencyWidth,int pixelSaliencyHeight,ref List<float> maxRegionalSaliencyEveryFrame,bool ifNormalize)
    {//X right, y down
        if (pixelSaliency == null || pixelSaliency.Count == 0)
        {
            Debug.Log("no pixel saliency");
            return null;
        }
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        GetRectsOfFOV();
        var re = new List<float[,]>();
        var time1 = DateTime.Now.Ticks;        
        int frameStep = 1;
        bool ifFirstRegionalSaliency = maxRegionalSaliencyEveryFrame == null ? true : false;
        if (ifFirstRegionalSaliency)
            maxRegionalSaliencyEveryFrame = new List<float>();
        for (var frame = 0; frame < pixelSaliency.Count; frame += frameStep)
        {
            var tmpRegionalSaliency = new float[downsampleWidth, downsampleHeight];
            var prefixSumSaliency = new float[downsampleWidth, downsampleHeight];
            int wSeg = pixelSaliencyWidth / downsampleWidth;
            int hSeg = pixelSaliencyHeight / downsampleHeight;
            for (int i = 0; i < downsampleWidth; i++)
                for (int j = 0; j < downsampleHeight; j++)
                {
                    float tmp = 0;
                    for (int ii = 0; ii < wSeg; ii++)
                        for (int jj = 0; jj < hSeg; jj++) {
                            tmp += pixelSaliency[frame][i * wSeg + ii, j * hSeg + jj];
                        }
                    tmp /= wSeg * hSeg;
                    var x = Get2dPrefixSumValue(i - 1, j, prefixSumSaliency);
                    var y = Get2dPrefixSumValue(i, j - 1, prefixSumSaliency);
                    var z = Get2dPrefixSumValue(i - 1, j - 1, prefixSumSaliency);
                    prefixSumSaliency[i, j] = x + y - z + Mathf.Min(tmp * pixelSaliencyEnhanceWeight, 1);//Prefix Sum
                }
            for (int i = 0; i < downsampleWidth; i++)
                for (int j = 0; j < downsampleHeight; j++)
                {
                    var rect = rectOfPixel[i, j];
                    var xRangeList = RangeX(rect.xMin, rect.xMax, downsampleWidth);
                    var yRange = RangeY(rect.yMin, rect.yMax, downsampleHeight);
                    float score = 0;
                    foreach (var h in xRangeList)
                    {
                        var v = yRange;
                        score += Get2dPrefixSumValue((int)h.y, (int)v.y, prefixSumSaliency)
                            - Get2dPrefixSumValue((int)h.y, (int)v.x - 1, prefixSumSaliency)
                            - Get2dPrefixSumValue((int)h.x - 1, (int)v.y, prefixSumSaliency)
                            + Get2dPrefixSumValue((int)h.x - 1, (int)v.x - 1, prefixSumSaliency);
                    }
                    tmpRegionalSaliency[i, j] = score / (GetRectWidth(rect) * GetRectHeight(rect));
                }
            float maxV = 0;
            re.Add(tmpRegionalSaliency);
        }
        var time2 = DateTime.Now.Ticks;
        var timeCost = (time2 - time1) / 10000000;
        Debug.Log("PreprocessRegionalSaliency time cost: " + timeCost);
        Debug.Log("PreprocessRegionalSaliency end.......");
        Debug.Log("regionalSaliency.Count: " + re.Count);
        return re;
    }

    //Normalize the out of bounds pixels to the range
    public static void NormalizePixelInRange(ref int x, ref int y,int width,int height)
    {
        x = (x % width + width) % width;
        if (y < 0)
            y = 0;
        else if (y >= height)
            y = height - 1;
    }    
    public static void NormalizePixelInRange(ref float x, ref float y, int width, int height)
    {
        x = (x % width + width) % width;
        if (y < 0)
            y = 0;
        else if (y >= height)
            y = height - 1;
    }

    //Output the path to the console
    private void OutputPathToDebugLog(List<Vector2> p, string pathName)
    {
        string content = "";
        for (int i = 0; i < p.Count; i++)
        {
            content += p[i];
            if (i < p.Count - 1)
                content += "->";
        }
        Debug.Log(pathName + ".Count: " + p.Count);
        Debug.Log("Path: " + content);
    }

    
    public Vector2 GetVector2Of2Pixels(Vector2 p1, Vector2 p2, float w)
    {        
        float vx = p2.x - p1.x;
        if (vx > w / 2)
            vx -= w;
        else if (vx < -w / 2)
            vx += w;
        return new Vector2(vx, p2.y - p1.y);
    }
    public float L1Norm(Vector2 v) {
        return Mathf.Abs(v.x) + Mathf.Abs(v.y);
    }

    
    private void NormalizePixelSaliencyTo0_1(float [,]ps,int width,int height) {
        float maxV = 0;
        for (int i = 0; i < width; i++)
            for (int j = 0; j < height; j++)
                maxV = Mathf.Max(maxV, ps[i, j]);
        if (maxV > 0)
            for (int i = 0; i < width; i++)
                for (int j = 0; j < height; j++)
                    ps[i, j] /= maxV;
    }
    private void NormalizePixelSaliencyTo0_1(List<float[,]> ps, int width, int height)
    {
        float maxV = 0;
        for (int frame = 0; frame < ps.Count; frame++)
            for (int i = 0; i < width; i++)
                for (int j = 0; j < height; j++)
                    maxV = Mathf.Max(maxV, ps[frame][i, j]);
        if (maxV > 0)
            for (int frame = 0; frame < ps.Count; frame++)
                for (int i = 0; i < width; i++)
                    for (int j = 0; j < height; j++)
                        ps[frame][i, j] /= maxV;
    }

    public List<float[,]> GetDownsamplePixelMask(int downsampleWidth, int downsampleHeight, bool ifUseNormalization)
    {
        var downsamplePixelMask = new List<float[,]>();
        if (pixelMaskList != null && blendMaskWeight != 0)
        {
            var wSeg = MenuBarController.pixelMaskWidth / downsampleWidth;
            var hSeg = MenuBarController.pixelMaskHeight / downsampleHeight;
            for (int frame = 0; frame < pixelMaskList.Count; frame++)
            {
                var tmp = new float[downsampleWidth, downsampleHeight];
                for (int i = 0; i < downsampleWidth; i++)
                    for (int j = 0; j < downsampleHeight; j++)
                    {
                        var sum = 0f;
                        for (int ii = 0; ii < wSeg; ii++)
                            for (int jj = 0; jj < hSeg; jj++)
                                sum += pixelMaskList[frame][i * wSeg + ii, j * hSeg + jj];
                        sum /= wSeg * hSeg;
                        tmp[i, j] = sum;
                    }
                if (ifUseNormalization)
                    if (!ifUseAllFramePixelSaliencyMaxNormalize)
                        NormalizePixelSaliencyTo0_1(tmp, downsampleWidth, downsampleHeight);
                downsamplePixelMask.Add(tmp);
            }
        }
        if (ifUseNormalization)
            if (ifUseAllFramePixelSaliencyMaxNormalize)
                NormalizePixelSaliencyTo0_1(downsamplePixelMask, downsampleWidth, downsampleHeight);
        return downsamplePixelMask;
    }
    public List<float[,]> GetDownsamplePixelSaliency(int downsampleWidth, int downsampleHeight, bool ifUseNormalization) {                
        var downsamplePixelSaliency = new List<float[,]>();
        var chosenPixelSaliency = ifUseNormalizationPixelSaliency ? normalizedPixelSaliency : pixelSaliency;
        if (chosenPixelSaliency != null) {
            var wSeg = MenuBarController.pixelSaliencyWidth / downsampleWidth;
            var hSeg = MenuBarController.pixelSaliencyHeight / downsampleHeight;
            for (int frame = 0; frame < chosenPixelSaliency.Count; frame++)
            {
                var tmp = new float[downsampleWidth, downsampleHeight];                
                for (int i = 0; i < downsampleWidth; i++)
                    for (int j = 0; j < downsampleHeight; j++)
                    {
                        var sum = 0f;
                        for (int ii = 0; ii < wSeg; ii++)
                            for (int jj = 0; jj < hSeg; jj++)
                                sum += chosenPixelSaliency[frame][i * wSeg + ii, j * hSeg + jj];
                        sum /= wSeg * hSeg;
                        tmp[i, j] = Mathf.Min(sum * pixelSaliencyEnhanceWeight, 1);
                    }
                downsamplePixelSaliency.Add(tmp);
            }
        }
        if (ifUseNormalization)
            if (ifUseAllFramePixelSaliencyMaxNormalize)
                NormalizePixelSaliencyTo0_1(downsamplePixelSaliency, downsampleWidth, downsampleHeight);
        return downsamplePixelSaliency;
    }

    public Vector2[,] GetDownsamplePixelOpticalFlow(Vector2[,] pixelOpticalFlow, int downsampleWidth, int downsampleHeight, bool ifRound) {
        var re = new Vector2[downsampleWidth, downsampleHeight];
        var wSeg = MenuBarController.pixelOpticalFlowWidth / downsampleWidth;
        var hSeg = MenuBarController.pixelOpticalFlowHeight / downsampleHeight;
        for (int i = 0; i < downsampleWidth; i++)
            for (int j = 0; j < downsampleHeight; j++)
            {
                var sum = Vector2.zero;
                for (int ii = 0; ii < wSeg; ii++)
                    for (int jj = 0; jj < hSeg; jj++)
                        sum += pixelOpticalFlow[i * wSeg + ii, j * hSeg + jj];
                sum /= wSeg * hSeg;
                sum.x /= wSeg;
                sum.y /= hSeg;
                if (ifRound) {
                    sum.x = Mathf.Round(sum.x);
                    sum.y = Mathf.Round(sum.y);
                }
                re[i, j] = sum;
            }
        return re;
    }
    public List<Vector2[,]> GetDownsamplePixelOpticalFlow(int downsampleWidth, int downsampleHeight, bool ifRound)
    {
        var downsamplePixelOpticalFlow = new List<Vector2[,]>();
        if (pixelOpticalFlow != null) {
            for (int frame = 0; frame < pixelOpticalFlow.Count; frame++)
            {
                var tmp = GetDownsamplePixelOpticalFlow(pixelOpticalFlow[frame], downsampleWidth, downsampleHeight, ifRound);
                downsamplePixelOpticalFlow.Add(tmp);
            }
        }
        return downsamplePixelOpticalFlow;
    }

    private float GaussianFunction(float x0,float y0,float x,float y, float thetax, float thetay) {
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        float A = 1;                
        float xSeg = Mathf.Min(Mathf.Abs(x - x0), downsampleWidth - Mathf.Abs(x - x0));
        float ySeg = y - y0;
        float n = -(xSeg * xSeg / (2 * thetax * thetax) + ySeg * ySeg / (2 * thetay * thetay));
        return A * Mathf.Exp(n);
    }
    private float GaussianFunction(Vector2 p0, Vector2 p, float theta)
    {
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        float A = 1;
        float xSeg = Mathf.Min(Mathf.Abs(p.x - p0.x), downsampleWidth - Mathf.Abs(p.x - p0.x));
        float ySeg = p.y - p0.y;
        float n = -(xSeg * xSeg + ySeg * ySeg) / (2 * theta * theta);
        return A * Mathf.Exp(n);
    }

    private float RectRectIoU(Rect a, Rect b) {
        var singleAreaA = GetRectWidth(a) * GetRectHeight(a);
        var singleAreaB = GetRectWidth(b) * GetRectHeight(b);        
        float twoOverlapArea= GetRectRectOverlapArea(a, b);

        return twoOverlapArea / (singleAreaA + singleAreaB - twoOverlapArea);
    }

    private float PixelSaliencyDecay(int x0, int y0, int x,int y, float s) {
        float re = s;

        //If the field intersection IOU is greater than how much, it will be cleared
        var r0 = rectOfPixel[x0, y0];
        var r = rectOfPixel[x, y];
        if (RectRectIoU(r0, r) > greedyIouThreshold)
            re = 0;

        return re;
    }
    public void UpdateMethod3PixelSaliencyAndOpticalFlowTextureList() {
        int dpWidth = 9;
        int dpHeight = 5;
        Manager.DestroyTexture2dList(method3PixelSaliencyTextureList);
        Manager.DestroyTexture2dList(method3OpticalFlowTextureList);
        if (!ifInitPathUseGreedy && initialPath != null && initialPath.Length != 0)
        {
            int totalKeyFrame = pixelSaliency.Count;
            method3OpticalFlowTextureList = new List<Texture2D>();
            method3PixelSaliencyTextureList = new List<Texture2D>();            
            int downsampleWidth, downsampleHeight;
            GetDownsampleSize(out downsampleWidth, out downsampleHeight);
            var downsamplePixelBlend = GetDownsamplePixelBlendSaliencyMask(downsampleWidth, downsampleHeight, ifUseNormalizationPixelSaliency);
            var downsamplePixelOpticalFlow = GetDownsamplePixelOpticalFlow(downsampleWidth, downsampleHeight, true);
            for (int frame = 0; frame < totalKeyFrame; frame++) {
                float[,] blend = null;
                Vector2[,] opticalFlow = null;
                GetMethod3BlendAndOpticalFlow(downsamplePixelBlend, downsamplePixelOpticalFlow, frame, dpWidth, dpHeight, out blend, out opticalFlow);
                var oft = panelVideoController.VisualizeOpticalFlow(opticalFlow, dpWidth, dpHeight);
                var pst = panelVideoController.VisualizeSaliencyOrMask(blend, dpWidth, dpHeight, panelVideoController.ifNormalizeShowSaliency);
                method3OpticalFlowTextureList.Add(oft);
                method3PixelSaliencyTextureList.Add(pst);
            }
        }
    }
    public void UpdateEveryCameraPixelSaliencyTextureList() {        
        if (ifInitPathUseGreedy&& initialPath!=null&& initialPath.Length!=0)
        {
            everyCameraDownsamplePixelSaliency = new List<List<float[,]>>();
            List<float[,]> downsamplePixelBlend = null;
            int downsampleWidth, downsampleHeight;
            GetDownsampleSize(out downsampleWidth, out downsampleHeight);
            int w = downsampleWidth, h = downsampleHeight;
            if (panelVideoController.ifShowOriginSaliencyMaskOpticalFlow) {
                w = MenuBarController.pixelSaliencyWidth;
                h = MenuBarController.pixelSaliencyHeight;
            }
            int wSeg = w / downsampleWidth;
            int hSeg = h / downsampleHeight;
            downsamplePixelBlend = GetDownsamplePixelBlendSaliencyMask(w, h, ifUseNormalizationPixelSaliency);            
            for (int camId = 0; camId < initialPath.Length; camId++) {
                var tmp = new List<float[,]>();
                for (int frame = 0; frame < downsamplePixelBlend.Count; frame++)
                {
                    var tmpPS = new float[w, h];
                    for (int i = 0; i < w; i++)
                        for (int j = 0; j < h; j++)
                            tmpPS[i, j] = downsamplePixelBlend[frame][i, j];
                    tmp.Add(tmpPS);
                }
                for (int frame = 0; frame < downsamplePixelBlend.Count; frame++)
                {
                    var pixel = initialPath[camId][frame];
                    int x = (int)pixel.x;
                    int y = (int)pixel.y;
                    NormalizePixelInRange(ref x, ref y, downsampleWidth, downsampleHeight);
                    for (int i = 0; i < downsampleWidth; i++)
                        for (int j = 0; j < downsampleHeight; j++)
                        {
                            var tmpV = PixelSaliencyDecay(x, y, i, j, 1);
                            if (tmpV == 0) {
                                for (int ii = 0; ii < wSeg; ii++)
                                    for (int jj = 0; jj < hSeg; jj++) {
                                        downsamplePixelBlend[frame][i * wSeg + ii, j * hSeg + jj] = 0;
                                    }
                            }
                        }
                }
                everyCameraDownsamplePixelSaliency.Add(tmp);
            }
        }
        else {
            everyCameraDownsamplePixelSaliency = null;
        }
        panelVideoController.PrepareEveryCameraPixelSaliencyTextures();
    }
    private void InitialPathPlanning()
    {                
        if (pixelSaliency == null || pixelSaliency.Count == 0)
            return;
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var downsamplePixelBlend = GetDownsamplePixelBlendSaliencyMask(downsampleWidth, downsampleHeight, ifUseNormalizationPixelSaliency);
        var downsamplePixelOpticalFlow = GetDownsamplePixelOpticalFlow(downsampleWidth, downsampleHeight, true);

        //Only the first normalized PixelSaliency is used to determine what time to stop adding new cameras.
        var noNormalizePixelBlend = GetDownsamplePixelBlendSaliencyMask(downsampleWidth, downsampleHeight, false);
        //If the maximum PS is less than this value, the camera will stop increasing

        everyCameraDownsamplePixelSaliency = new List<List<float[,]>>();
        var updateRadius = 15;//Radius used to update the status
        var w0 = greedyW0;
        if (useGpu)
        {
            InitBuffer();
            initialPathPlanningComputeShader.SetInt("downsampleWidth", downsampleWidth);
            initialPathPlanningComputeShader.SetInt("downsampleHeight", downsampleHeight);
            initialPathPlanningComputeShader.SetInt("updateRadius", updateRadius);
            initialPathPlanningComputeShader.SetFloat("w0", w0);
            initialPathPlanningComputeShader.SetFloat("w1", w1);
            initialPathPlanningComputeShader.SetFloat("wop", wop);
            initialPathPlanningComputeShader.SetFloat("preCamPosTheta", preCamPosTheta);
            initialPathPlanningComputeShader.SetFloat("preCamVelTheta", preCamVelTheta);
            initialPathPlanningComputeShader.SetBool("ifAddCamSimilarity", ifAddCamSimilarity);
        }
        var noNormalizePs = 1f;
        var maxCamNum = 10;
        var initialPathTmp = new List<List<Vector2>>();

        int beginCamId = 0;
        int endCamNum = panelVideoController.cameraGroupNum;
        if (ifAutoDetermineCamNum) {            
            endCamNum = maxCamNum;
        }
        int camId;
        for (camId = beginCamId; (!ifAutoDetermineCamNum || noNormalizePs > pixelSaliencyThreshold) && camId< endCamNum; camId++)
        {
            initialPathTmp.Add(new List<Vector2>());
            var f = new List<float[,]>();
            var p = new List<Vector2[,]>();
            var nowF = new float[downsampleWidth, downsampleHeight];
            var nowP = new Vector2[downsampleWidth, downsampleHeight];
            for (int i = 0; i < downsampleWidth; i++)
                for (int j = 0; j < downsampleHeight; j++)
                    nowF[i, j] = Mathf.Abs(1 - downsamplePixelBlend[0][i, j]);
            f.Add(nowF);
            p.Add(nowP);

            if (useGpu) {
                preCameraPreKeyFramePosComputeBuffer = new ComputeBuffer(Mathf.Max(camId, 1), 8);//Length cannot be 0
                preCameraNowKeyFramePosComputeBuffer = new ComputeBuffer(Mathf.Max(camId, 1), 8);
                initialPathPlanningComputeShader.SetBuffer(kernel, "preCameraPreKeyFramePos", preCameraPreKeyFramePosComputeBuffer);
                initialPathPlanningComputeShader.SetBuffer(kernel, "preCameraNowKeyFramePos", preCameraNowKeyFramePosComputeBuffer);
                initialPathPlanningComputeShader.SetInt("camId", camId);
            }

            for (int frame = 1; frame < downsamplePixelBlend.Count; frame++)
            {
                var preF = f[f.Count - 1];
                nowF = new float[downsampleWidth, downsampleHeight];
                nowP = new Vector2[downsampleWidth, downsampleHeight];
                if (useGpu)
                {
                    preFComputeBuffer.SetData(preF);
                    downsamplePixelSaliencyComputeBuffer.SetData(downsamplePixelBlend[frame]);
                    downsamplePixelOpticalFlowComputeBuffer.SetData(downsamplePixelOpticalFlow[frame - 1]);

                    //Set the path of the previous camera to calculate the cost of path duplication
                    var preCameraPreKeyFramePos = new Vector2[camId];
                    var nowCameraPreKeyFramePos = new Vector2[camId];
                    for (int i = 0; i < camId; i++) {
                        preCameraPreKeyFramePos[i] = initialPathTmp[i][frame - 1];
                        nowCameraPreKeyFramePos[i] = initialPathTmp[i][frame];

                    }
                    preCameraPreKeyFramePosComputeBuffer.SetData(preCameraPreKeyFramePos);
                    preCameraNowKeyFramePosComputeBuffer.SetData(nowCameraPreKeyFramePos);

                    initialPathPlanningComputeShader.Dispatch(kernel, 1, 1, downsampleWidth * downsampleHeight / 6 / 6);
                    nowFComputeBuffer.GetData(nowFDataReceiver);
                    nowPComputeBuffer.GetData(nowPDataReceiver);
                    for (int s = 0; s < downsampleWidth * downsampleHeight;s++) {
                        int i = s / downsampleHeight;
                        int j = s % downsampleHeight;
                        int preS = nowPDataReceiver[s];
                        nowF[i, j] = nowFDataReceiver[s];
                        nowP[i, j] = new Vector2(preS / downsampleHeight, preS % downsampleHeight);
                    }
                }
                else {
                    Debug.LogError("Go and get a GPU!");
                }
                f.Add(nowF);
                p.Add(nowP);
            }
            var minF = oo;
            var id = f.Count - 1;
            var nowPixel = new Vector2();
            for (int i = 0; i < downsampleWidth; i++)
                for (int j = 0; j < downsampleHeight; j++)
                    if (minF > f[id][i, j])
                    {
                        minF = f[id][i, j];
                        nowPixel = new Vector2(i, j);
                    }
            while (id >= 0)
            {
                initialPathTmp[camId].Add(nowPixel);
                nowPixel = p[id][(int)nowPixel.x, (int)nowPixel.y];
                id--;
            }
            initialPathTmp[camId].Reverse();
            Debug.Log("minF: " + minF);

            var tmp = new List<float[,]>();
            for (int frame = 0; frame < downsamplePixelBlend.Count; frame++) {
                var tmpPS = new float[downsampleWidth, downsampleHeight];
                for (int i = 0; i < downsampleWidth; i++)
                    for (int j = 0; j < downsampleHeight; j++)
                        tmpPS[i, j] = downsamplePixelBlend[frame][i, j];
                tmp.Add(tmpPS);
            }
            everyCameraDownsamplePixelSaliency.Add(tmp);

            if (useGpu) {
                preCameraPreKeyFramePosComputeBuffer.Release();
                preCameraNowKeyFramePosComputeBuffer.Release();
            }

            for (int frame = 0; frame < downsamplePixelBlend.Count; frame++) {
            var pixel = initialPathTmp[camId][frame];
            int x = (int)pixel.x;
                int y = (int)pixel.y;
                NormalizePixelInRange(ref x,ref y, downsampleWidth, downsampleHeight);                    
                for (int i = 0; i < downsampleWidth; i++)
                    for (int j = 0; j < downsampleHeight; j++)
                    {
                        downsamplePixelBlend[frame][i, j] = PixelSaliencyDecay(x, y, i, j, downsamplePixelBlend[frame][i, j]);
                    }
            }
            noNormalizePs = 0;
            for (int frame = 0; frame < noNormalizePixelBlend.Count; frame++)
            {
                var pixel = initialPathTmp[camId][frame];
                int x = (int)pixel.x;
                int y = (int)pixel.y;
                NormalizePixelInRange(ref x, ref y, downsampleWidth, downsampleHeight);
                noNormalizePs += noNormalizePixelBlend[frame][x, y];
                for (int i = 0; i < downsampleWidth; i++)
                    for (int j = 0; j < downsampleHeight; j++)
                    {
                        noNormalizePixelBlend[frame][i, j] = PixelSaliencyDecay(x, y, i, j, noNormalizePixelBlend[frame][i, j]);
                    }
            }
            noNormalizePs /= noNormalizePixelBlend.Count;
        }
        if (useGpu)
        {
            ReleaseBuffer();
        }
        initialPath = new List<Vector2>[initialPathTmp.Count];
        for (int i = 0; i < initialPathTmp.Count; i++)
            initialPath[i] = initialPathTmp[i];
        panelVideoController.UpdateCameraNum(Mathf.Max(camId, 1));
        Debug.Log("initial path cameraNum: " + camId);        
        fovAwarePath = initialPath;//Temporarily take fovawarepath to be consistent with initialpath
    }
    private List<int> DecodeStatus(int status,int camGroupNum,int posNum) {
        var re = new List<int>();
        for (int i = 0; i < camGroupNum; i++) {
            re.Add(status % posNum);
            status /= posNum;
        }
        return re;
    }

    //The width and height of rect are not the actual width and height
    public float GetRectRectOverlapArea(Rect a,Rect b) {
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var re = 0f;
        var axRangeList = RangeX(a.min.x, a.max.x, downsampleWidth);
        var ayRange = RangeY(a.min.y, a.max.y, downsampleHeight);
        var bxRangeList = RangeX(b.min.x, b.max.x, downsampleWidth);
        var byRange = RangeY(b.min.y, b.max.y, downsampleHeight);
        float xSeg = 0;
        float ySeg = 0;
        float l, r;
        foreach (var axRange in axRangeList)
            foreach (var bxRange in bxRangeList) {
                l = Mathf.Max(axRange.x, bxRange.x);
                r = Mathf.Min(axRange.y, bxRange.y);
                xSeg += Mathf.Max(0, r - l + 1);
            }
        l = Mathf.Max(ayRange.x, byRange.x);
        r = Mathf.Min(ayRange.y, byRange.y);
        ySeg += Mathf.Max(0, r - l + 1);
        re = xSeg * ySeg;
        return re;
    }

    private float GetSalEnergy(int cameraGroupNum,List<int> camPosList, List<Vector3> candidatePos) {
        var re = 0f;
        for (int i = 0; i < cameraGroupNum; i++)
        {
            re += Mathf.Abs(1 - candidatePos[camPosList[i]].z);
        }
        return re;
    }

    private float GetOverlapEnergy(List<int> cameraPosList,List<Vector3> candidatePos, int cameraGroupNum,float overlapWeight) {
        var re = 0f;
        for (int i = 0; i < cameraGroupNum; i++)
            for (int j = i + 1; j < cameraGroupNum; j++)
            {
                var cp1 = candidatePos[cameraPosList[i]];
                var cp2 = candidatePos[cameraPosList[j]];
                var a = rectOfPixel[(int)cp1.x, (int)cp1.y];
                var b = rectOfPixel[(int)cp2.x, (int)cp2.y];
                re += overlapWeight * GetRectRectOverlapArea(a, b);
            }
        return re;
    }

    
    private void InitialPathPlanningMethod2()
    {
        if (pixelSaliency == null || pixelSaliency.Count == 0)
            return;
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var camGroupNum = panelVideoController.cameraGroupNum;
        initialPath = new List<Vector2>[camGroupNum];
        var downsamplePixelSaliency = GetDownsamplePixelSaliency(downsampleWidth, downsampleHeight, true);
        var downsamplePixelOpticalFlow = GetDownsamplePixelOpticalFlow(downsampleWidth, downsampleHeight, true);
        int totalFrameNum = pixelSaliency.Count;        

        
        var candidatePosList = new List<List<Vector3>>();
        var maxPosNum = 30;
        for (int frame = 0; frame < totalFrameNum; frame++) {
            var status = new List<Vector3>();
            for (int posId = 0; posId < maxPosNum; posId++) {
                var maxV = 0f;
                var posAndSal = new Vector3();
                for (int i = 0; i < downsampleWidth; i++)
                    for (int j = 0; j < downsampleHeight; j++)
                    {
                        if (maxV < downsamplePixelSaliency[frame][i, j])
                        {
                            maxV = Mathf.Max(maxV, downsamplePixelSaliency[frame][i, j]);
                            posAndSal = new Vector3(i, j, maxV);
                        }
                    }
                if (maxV == 0)
                    break;
                status.Add(posAndSal);
                var pixel = new Vector2(posAndSal.x, posAndSal.y);
                int x = (int)pixel.x;
                int y = (int)pixel.y;
                NormalizePixelInRange(ref x, ref y, downsampleWidth, downsampleHeight);
                var rect = rectOfPixel[x, y];                
                for (int i = (int)rect.min.x+  GetRectWidth(rect)/4; i <= rect.max.x - GetRectWidth(rect) / 4; i++)
                    for (int j = (int)rect.min.y + GetRectHeight(rect) / 4; j <= rect.max.y - GetRectHeight(rect) / 4; j++)
                    {
                        int xx = i;
                        int yy = j;
                        NormalizePixelInRange(ref xx, ref yy, downsampleWidth, downsampleHeight);
                        downsamplePixelSaliency[frame][xx, yy] = 0;
                    }
            }
            if (status.Count == 0)
            {
                if (frame == 0)
                {
                    
                    status.Add(new Vector3(downsampleWidth / 2, downsampleHeight / 2, 0));
                }
                else {
                    
                    status = candidatePosList[frame - 1];
                }
            }
            Debug.Log("frame: "+frame+", statusNum: "+status.Count);
            foreach (var s in status)
                Debug.Log(s);
            candidatePosList.Add(status);
        }

        var f = new List<float[]>();
        var p = new List<int[]>();
        var updateRadius = 15;
        var overlapWeight = 1f;

        for (int frame = 0; frame < totalFrameNum; frame++) {
            var nowCandidatePos = candidatePosList[frame];
            var nowf = new float[camGroupNum * nowCandidatePos.Count];
            var nowp = new int[camGroupNum * nowCandidatePos.Count];
            if (frame == 0)
            {
                for (int status = 0; status < camGroupNum * nowCandidatePos.Count; status++)
                {
                    var camPosList = DecodeStatus(status, camGroupNum, nowCandidatePos.Count);
                    var salE = GetSalEnergy(camGroupNum, camPosList, nowCandidatePos);                    
                    var overlapE = GetOverlapEnergy(camPosList, nowCandidatePos, camGroupNum, overlapWeight);
                    nowf[status] = salE + overlapE;
                    nowp[status] = 0;
                }
            }
            else {
                var pref = f[f.Count - 1];
                var preCandidatePos = candidatePosList[frame - 1];
                for (int nowStatus = 0; nowStatus < camGroupNum * nowCandidatePos.Count; nowStatus++) {
                    nowf[nowStatus] = oo;
                    nowp[nowStatus] = 0;
                    var nowCamPosList = DecodeStatus(nowStatus, camGroupNum, nowCandidatePos.Count);
                    var salE = GetSalEnergy(camGroupNum, nowCamPosList, nowCandidatePos);
                    var overlapE = GetOverlapEnergy(nowCamPosList, nowCandidatePos, camGroupNum, overlapWeight);
                    for (int preStatus = 0; preStatus < camGroupNum * preCandidatePos.Count; preStatus++)
                    {
                        var preCamPosList = DecodeStatus(preStatus, camGroupNum, preCandidatePos.Count);
                        var velocityE = 0f;
                        for (int camId = 0; camId < camGroupNum; camId++) {
                            float value;
                            int ii = (int)preCandidatePos[preCamPosList[camId]].x;
                            int jj = (int)preCandidatePos[preCamPosList[camId]].y;
                            int i = (int)nowCandidatePos[nowCamPosList[camId]].x;
                            int j = (int)nowCandidatePos[nowCamPosList[camId]].y;
                            var v = GetVector2Of2Pixels(new Vector2(ii, jj), new Vector2(i, j), downsampleWidth);
                            if (downsamplePixelOpticalFlow == null || downsamplePixelOpticalFlow.Count == 0)
                                value = w0 * L1Norm(v);
                            else
                                value = w0 * L1Norm(v - downsamplePixelOpticalFlow[frame - 1][ii, jj]);
                            velocityE += value;
                        }
                        var newV = salE + overlapE + velocityE + pref[preStatus];
                        if (nowf[nowStatus] > newV) {
                            nowf[nowStatus] = newV;
                            nowp[nowStatus] = preStatus;
                        }                        
                    }
                }
            }

            f.Add(nowf);
            p.Add(nowp);
        }
        var minF = oo;
        var chosenStatus = 0;
        var nowFrame = totalFrameNum - 1;
        var candidatePos = candidatePosList[nowFrame];
        for (int i = 0; i < camGroupNum * candidatePos.Count; i++)
            if (f[nowFrame][i] < minF) {
                minF = f[nowFrame][i];
                chosenStatus = i;
            }
        Debug.Log("minF: "+minF);
        for (int camId = 0; camId < camGroupNum; camId++) {
            initialPath[camId] = new List<Vector2>();
        }
        for (; nowFrame >= 0; nowFrame--) {
            candidatePos = candidatePosList[nowFrame];
            var camPosList = DecodeStatus(chosenStatus, camGroupNum, candidatePos.Count);
            for (int camId = 0; camId < camGroupNum; camId++)
            {
                initialPath[camId].Add(new Vector2(candidatePos[camPosList[camId]].x, candidatePos[camPosList[camId]].y));
            }
            Debug.Log(string.Format("nowFrame: {0}, minF: {1}", nowFrame, f[nowFrame][chosenStatus]));
            chosenStatus = p[nowFrame][chosenStatus];            
        }
        for (int camId = 0; camId < camGroupNum; camId++)
        {
            initialPath[camId].Reverse();
        }
    }
    //x width, y height
    private List<Vector2> DecodeMethod3Status(int s,int camGroupNum,int dpWidth,int dpHeight) {
        var re = new List<Vector2>();
        for (int i = 0; i < camGroupNum; i++) {
            var tmp = s % (dpWidth * dpHeight);
            re.Add(new Vector2(tmp / dpHeight, tmp % dpHeight));
            s /= dpWidth * dpHeight;
        }
        return re;
    }
    //Get the prestatuslist according to Campos
    private void GetPreStatusList(int camId,int preStatus, List<Vector2> camPosList,int dpWidth,int dpHeight,int transferRadius,ref List<int> preStatusList) {
        if (camId < 0) {
            preStatusList.Add(preStatus);
            return;
        }
        int x = (int)camPosList[camId].x;
        int y = (int)camPosList[camId].y;
        for (int i = x - transferRadius; i <= x + transferRadius; i++)
            for (int j = y - transferRadius; j <= y + transferRadius; j++) {
                if (j < 0 || j >= dpHeight)
                    continue;
                int xx = i;
                int yy = j;
                NormalizePixelInRange(ref xx, ref yy, dpWidth, dpHeight);
                var nextPreStatus = (preStatus * dpWidth + xx) * dpHeight + yy;
                GetPreStatusList(camId - 1, nextPreStatus, camPosList, dpWidth, dpHeight, transferRadius, ref preStatusList);
            }
    }

    private List<Vector2>[] GetMethod3RealInitialPath(List<float[,]> downsamplePixelSaliency,List<Vector2[,]> downsamplePixelOpticalFlow, int dpWidth,int dpHeight,List<Vector2>[] dpPaths) {
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var wSeg = downsampleWidth / dpWidth;
        var hSeg = downsampleHeight / dpHeight;
        Debug.Log(string.Format("dpWidth,dpHeight: {0},{1}", dpWidth, dpHeight));
        Debug.Log(string.Format("wSeg,hSeg: {0},{1}", wSeg, hSeg));        
        var camGroupNum = panelVideoController.cameraGroupNum;
        var initialPath = new List<Vector2>[camGroupNum];
        //Find the initial path in the reduced search range              
        for (int camId = 0; camId < camGroupNum; camId++)
        {
            var dpPath = dpPaths[camId];
            initialPath[camId] = new List<Vector2>();
            var f = new List<float[,]>();
            var p = new List<Vector2[,]>();

            for (int frame = 0; frame < downsamplePixelSaliency.Count; frame++)
            {                
                var nowF = new float[wSeg, hSeg];
                var nowP = new Vector2[wSeg, hSeg];
                if (frame == 0)
                {
                    for (int i = 0; i < wSeg; i++)
                        for (int j = 0; j < hSeg; j++) {
                            int realI = (int)dpPath[frame].x * wSeg + i;
                            int realJ = (int)dpPath[frame].y * hSeg + j;                                                        
                            nowF[i, j] = Mathf.Abs(1 - downsamplePixelSaliency[0][realI, realJ]);
                        }                            
                }
                else {
                    var preF = f[frame - 1];
                    for (int i = 0; i < wSeg; i++)
                        for (int j = 0; j < hSeg; j++)
                        {
                            int realI = (int)dpPath[frame].x * wSeg + i;
                            int realJ = (int)dpPath[frame].y * hSeg + j;
                            nowF[i, j] = oo;
                            for (int k = 0; k < wSeg; k++)
                                for (int z = 0; z < hSeg; z++)
                                {
                                    int realK = (int)dpPath[frame - 1].x * wSeg + k;
                                    int realZ = (int)dpPath[frame - 1].y * hSeg + z;
                                    var vTmp = GetVector2Of2Pixels(new Vector2(realI, realJ), new Vector2(realK, realZ), downsampleWidth);
                                    float value;
                                    var v = GetVector2Of2Pixels(new Vector2(realK, realZ), new Vector2(realI, realJ), downsampleWidth);
                                    value = Mathf.Abs(1 - downsamplePixelSaliency[frame][realI, realJ]) + preF[k, z] + method3RefineW0 * L1Norm(v - downsamplePixelOpticalFlow[frame - 1][realK, realZ]);
                                    if (nowF[i, j] > value)
                                    {
                                        nowF[i, j] = value;
                                        nowP[i, j] = new Vector2(k, z);
                                    }
                                }
                        }
                }
                f.Add(nowF);
                p.Add(nowP);
            }
            var minF = oo;
            var nowFrame = f.Count - 1;
            var nowPixel = new Vector2();
            for (int i = 0; i < wSeg; i++)
                for (int j = 0; j < hSeg; j++)
                    if (minF > f[nowFrame][i, j])
                    {
                        minF = f[nowFrame][i, j];
                        nowPixel = new Vector2(i, j);
                    }
            while (nowFrame >= 0)
            {
                var oldPixel = dpPaths[camId][nowFrame];
                var realPixel = new Vector2(oldPixel.x * wSeg + nowPixel.x, oldPixel.y * hSeg + nowPixel.y);
                initialPath[camId].Add(realPixel);
                nowPixel = p[nowFrame][(int)nowPixel.x, (int)nowPixel.y];
                nowFrame--;
            }
            initialPath[camId].Reverse();
            Debug.Log("minF: " + minF);
        }
        return initialPath;
    }

    
    private List<Vector2>[] RefinePathMethod3(int preWidth, int preHeight, List<Vector2>[] prePaths, bool ifRoundOpticalFlow) {
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        int refineWidth = 2 * preWidth;
        int refineHeight = 2 * preHeight;
        int wSeg = downsampleWidth / refineWidth;
        int hSeg = downsampleHeight / refineHeight;
        var camGroupNum = panelVideoController.cameraGroupNum;
        int totalFrameNum = pixelSaliency.Count;
        var downsamplePixelSaliency = GetDownsamplePixelSaliency(downsampleWidth, downsampleHeight, true);

        float[] twoRectOverlapArea = new float[refineWidth * refineHeight * refineWidth * refineHeight];
        float[] singleRectArea = new float[refineWidth * refineHeight];
        for (int i = 0; i < refineWidth * refineHeight; i++)
        {
            int x = i / refineHeight;
            int y = i % refineHeight;
            x = x * wSeg + wSeg / 2;
            y = y * hSeg + hSeg / 2;
            Debug.Log(string.Format("x,y: {0},{1}", x, y));
            singleRectArea[i] = GetRectWidth(rectOfPixel[x, y]) * GetRectHeight(rectOfPixel[x, y]);
            float singleArea = singleRectArea[i];
            for (int j = 0; j < refineWidth * refineHeight; j++)
            {
                int xx = j / refineHeight;
                int yy = j % refineHeight;

                xx = xx * wSeg + wSeg / 2;
                yy = yy * hSeg + hSeg / 2;
                var area = GetRectRectOverlapArea(rectOfPixel[x, y], rectOfPixel[xx, yy]);
                twoRectOverlapArea[i * refineHeight * refineHeight + j] = area;
            }
        }

        var f = new List<float[]>();
        var path = new List<int[]>();
        int statusNum = 1;
        for (int i = 0; i < camGroupNum; i++)
            statusNum *= 2 * 2;        
        for (int frame = 0; frame < totalFrameNum; frame++)
        {
            Vector2[,] preOpticalFlow =null;
            if (frame > 0)//Use the optical flow of the previous frame
                preOpticalFlow  = GetDownsamplePixelOpticalFlow(pixelOpticalFlow[frame - 1], refineWidth, refineHeight, ifRoundOpticalFlow);
            var saliency = new float[refineWidth, refineHeight];
            float maxSal = 0f;
            for (int i = 0; i < refineWidth; i++)
                for (int j = 0; j < refineHeight; j++)
                {
                    var s = 0f;
                    for (int ii = i * wSeg; ii < (i + 1) * wSeg; ii++)
                        for (int jj = j * hSeg; jj < (j + 1) * hSeg; jj++)
                        {                            
                            s = Mathf.Max(s, downsamplePixelSaliency[frame][ii, jj]);
                        }
                    saliency[i, j] = s;
                    maxSal = Mathf.Max(maxSal, s);
                }
            if (maxSal != 0)
            {
                for (int i = 0; i < refineWidth; i++)
                    for (int j = 0; j < refineHeight; j++)
                    {
                        saliency[i, j] /= maxSal;
                    }
            }
            var nowF = new float[statusNum];
            var nowP = new int[statusNum];
            for (int status = 0; status < statusNum; status++) {
                float salE = 0;
                float ans = 1e9f;
                int p = 0;
                var camPosOffsetList = DecodeMethod3Status(status, camGroupNum, 2, 2);
                var camPosList = new List<Vector2>();
                for (int camId = 0; camId < camGroupNum; camId++)
                {
                    var offset = camPosOffsetList[camId];
                    var prePath = prePaths[camId][frame];
                    camPosList.Add(new Vector2(prePath.x * 2 + offset.x, prePath.y * 2 + offset.y));
                }
                for (int camId = 0; camId < camPosList.Count; camId++) {
                    var pos = camPosList[camId];
                    float s = saliency[(int)pos.x, (int)pos.y];
                    float c = 1;                    
                    for (int i = 0; i < camGroupNum; i++)
                    {
                        if (i == camId)
                            continue;
                        int id = ((int)pos.x * refineHeight + (int)pos.y) * refineWidth * refineHeight + ((int)camPosList[i].x * refineHeight + (int)camPosList[i].y);
                        float I = twoRectOverlapArea[id];
                        float U = singleRectArea[(int)pos.x * refineHeight + (int)pos.y] + singleRectArea[(int)camPosList[i].x * refineHeight + (int)camPosList[i].y];
                        U -= I;

                        float tmp = Mathf.Min(1, I / U * overlapWeight);
                        c = Mathf.Min(c, 1 - tmp);

                    }
                    salE += Mathf.Abs(1 - c * s);
                }
                if (frame == 0)
                {
                    ans = salE;                    
                }
                else
                {
                    for (int preStatus = 0; preStatus < statusNum; preStatus++) {
                        var preF = f[frame - 1][preStatus];
                        var preCamPosOffsetList = DecodeMethod3Status(preStatus, camGroupNum, 2, 2);
                        var preCamPosList = new List<Vector2>();
                        for (int camId = 0; camId < camGroupNum; camId++)
                        {
                            var offset = preCamPosOffsetList[camId];
                            var prePath = prePaths[camId][frame - 1];
                            preCamPosList.Add(new Vector2(prePath.x * 2 + offset.x, prePath.y * 2 + offset.y));
                        }
                        float velocityE = 0;

                        for (int camId = camGroupNum - 1; camId >= 0; camId--)
                        {
                            float value;
                            int ii = (int)preCamPosList[camId].x;
                            int jj = (int)preCamPosList[camId].y;
                            int i = (int)camPosList[camId].x;
                            int j = (int)camPosList[camId].y;
                            
                            var v= GetVector2Of2Pixels(preCamPosList[camId], camPosList[camId], refineWidth);

                            value = method3RefineW0 * L1Norm(v - preOpticalFlow [ii, jj]);
                            velocityE += value;                            
                        }
                        float newV = salE + velocityE + preF;
                        if (ans > newV)
                        {
                            ans = newV;
                            p = preStatus;
                        }
                    }
                }
                nowF[status] = ans;
                nowP[status] = p;
            }        
            f.Add(nowF);
            path.Add(nowP);
        }
        var minF = oo;
        var chosenStatus = 0;
        var nowFrame = f.Count - 1;
        for (int i = 0; i < statusNum; i++)
            if (f[nowFrame][i] < minF)
            {
                minF = f[nowFrame][i];
                chosenStatus = i;
            }
        Debug.Log("minF: " + minF);
        var refinePaths = new List<Vector2>[camGroupNum];
        for (int camId = 0; camId < camGroupNum; camId++)
        {
            refinePaths[camId] = new List<Vector2>();            
        }
        for (; nowFrame >= 0; nowFrame--)
        {
            var camPosOffsetList = DecodeMethod3Status(chosenStatus, camGroupNum, 2, 2);
            var camPosList = new List<Vector2>();
            for (int camId = 0; camId < camGroupNum; camId++)
            {
                var offset = camPosOffsetList[camId];
                var prePath = prePaths[camId][nowFrame];
                camPosList.Add(new Vector2(prePath.x * 2 + offset.x, prePath.y * 2 + offset.y));
            }
            for (int camId = 0; camId < camGroupNum; camId++)
            {
                refinePaths[camId].Add(new Vector2(camPosList[camId].x, camPosList[camId].y));
                var realP = new Vector2(camPosList[camId].x * wSeg + wSeg / 2, camPosList[camId].y * hSeg + hSeg / 2);
                
            }
            chosenStatus = path[nowFrame][chosenStatus];
        }
        for (int camId = 0; camId < camGroupNum; camId++)
        {
            refinePaths[camId].Reverse();            
        }        
        return refinePaths;
    }

    public class TopKComparer : IComparer<Vector3>//X is opticalflowlength, y is pixel validity; Z is ID, in ascending order (x first keyword, y second keyword)
    {
        public int Compare(Vector3 x, Vector3 y)
        {
            if (x.x == y.x) {
                if (x.y==y.y)
                    return 0;
                return x.y < y.y ? -1 : 1;
            }                
            return x.x < y.x ? -1 : 1;
        }
    }
    private float GetRestValuablePixelNum(List<Vector2>[] path,List<float[,]> noNormalizationDownsamplePixelBlend,List<Vector2[,]> downsamplePixelOpticalFlow) {
        int totalFrameNum = pixelSaliency.Count;
        var camGroupNum = panelVideoController.cameraGroupNum;
        float psSum = 0;
        int beginKeyFrame = beginKeyFrameOfGetRestValuablePixelNum;//From which frame does the calculation really begin
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        method3RestValuablePixelTextureList.Clear();
        for (int frame = 0; frame < totalFrameNum; frame++)
        {
            var t = new Texture2D(downsampleWidth,downsampleHeight);
            for (int i = 0; i < downsampleWidth; i++)
                for (int j = 0; j < downsampleHeight; j++) {
                    t.SetPixel(i, downsampleHeight - j - 1, Color.black);
                    if (noNormalizationDownsamplePixelBlend[frame][i, j] >= restMinimalPS&& downsamplePixelOpticalFlow[frame][i,j].magnitude>restMinimalOF&&frame>= beginKeyFrame) {
                        bool bl = true;
                        for (int camId = 0; camId < camGroupNum; camId++)
                        {
                            var pix = path[camId][frame];
                            var rect = rectOfPixel[(int)pix.x, (int)pix.y];
                            var largeRect = new Rect(rect.xMin - restMinimalXDist / 2, rect.yMin - restMinimalYDist / 2, rect.width + restMinimalXDist / 2, rect.height + restMinimalYDist / 2);
                            if (IfPixelInRect(new Vector2(i,j),largeRect,downsampleWidth,downsampleHeight))
                            {
                                bl = false;
                                break;
                            }
                        }
                        if (bl) {
                            psSum++;
                            t.SetPixel(i, downsampleHeight - j - 1, Color.white);
                        }                            
                    }
                }
            t.Apply();
            method3RestValuablePixelTextureList.Add(t);
        }
        psSum /= Mathf.Max(totalFrameNum - beginKeyFrame, 1);
        Debug.Log("rest valuable pixel num: "+ psSum);
        return psSum;
    }
    //Image filtering using Gaussian check
    public void UseGaussianKernel(float[,] data, int width,int height) {
        var tmp = new float[width, height];
        var rad = (gaussianKernelSize - 1) / 2;
        for (int i = 0; i < width; i++)
            for (int j = 0; j < height; j++) {
                float weightSum = 0;
                float valueSum = 0;
                for (int k = i-rad; k <= i+rad; k++)
                    for (int z = Mathf.Max(j-rad,0); z <= Mathf.Min(j+rad,height-1); z++)
                    {
                        int ii = k;
                        int jj = z;
                        NormalizePixelInRange(ref ii, ref jj, width, height);
                        var gaussianValue = GaussianFunction(i, j, ii, jj, filterTheta, filterTheta);
                        valueSum += gaussianValue * data[ii, jj];
                        weightSum += gaussianValue;
                    }
                if (weightSum == 0)
                    Debug.LogError("UseGaussianKernel: weightSum == 0");
                tmp[i, j] = valueSum / weightSum;
            }
        for (int i = 0; i < width; i++)
            for (int j = 0; j < height; j++)
                data[i, j] = tmp[i, j];
    }
    public List<float[,]> GetDownsamplePixelBlendSaliencyMask(List<float[,]> downsamplePixelSaliency,List<float[,]>downsamplePixelMask,int downsampleWidth,int downsampleHeight) {
        var re = new List<float[,]>();
        int len = downsamplePixelSaliency.Count;
        if (downsamplePixelMask != null)
            len = Mathf.Max(len, downsamplePixelMask.Count);
        for (int frame = 0; frame < len; frame++) {
            var tmp = new float[downsampleWidth, downsampleHeight];
            for (int i = 0; i < downsampleWidth; i++)
                for (int j = 0; j < downsampleHeight; j++) {
                    if (downsamplePixelSaliency.Count <= frame)
                        tmp[i, j] = downsamplePixelMask[frame][i, j];
                    else if (downsamplePixelMask==null || downsamplePixelMask.Count <= frame)
                        tmp[i, j] = downsamplePixelSaliency[frame][i, j];
                    else
                        tmp[i, j] = (downsamplePixelSaliency[frame][i, j] * blendSaliencyWeight + downsamplePixelMask[frame][i, j] * blendMaskWeight) / (blendSaliencyWeight + blendMaskWeight);
                }
            if (ifUseGaussianKernelToSmoothSaliency)
                UseGaussianKernel(tmp, downsampleWidth, downsampleHeight);
            re.Add(tmp);
        }
        return re;
    }
    public List<float[,]> GetDownsamplePixelBlendSaliencyMask(int downsampleWidth, int downsampleHeight, bool ifUseNormalization)
    {
        var downsamplePixelSaliency = GetDownsamplePixelSaliency(downsampleWidth, downsampleHeight, ifUseNormalization);
        var downsamplePixelMask = GetDownsamplePixelMask(downsampleWidth, downsampleHeight, ifUseNormalization);
        var re = new List<float[,]>();
        int len = Mathf.Max(downsamplePixelSaliency.Count, downsamplePixelMask.Count);
        for (int frame = 0; frame < len; frame++)
        {
            var tmp = new float[downsampleWidth, downsampleHeight];
            for (int i = 0; i < downsampleWidth; i++)
                for (int j = 0; j < downsampleHeight; j++)
                {
                    if (downsamplePixelSaliency.Count <= frame)
                        tmp[i, j] = downsamplePixelMask[frame][i, j];
                    else if (downsamplePixelMask.Count <= frame)
                        tmp[i, j] = downsamplePixelSaliency[frame][i, j];
                    else
                        tmp[i, j] = (downsamplePixelSaliency[frame][i, j] * blendSaliencyWeight + downsamplePixelMask[frame][i, j] * blendMaskWeight) / (blendSaliencyWeight + blendMaskWeight);
                }
            if (ifUseGaussianKernelToSmoothSaliency)
                UseGaussianKernel(tmp, downsampleWidth, downsampleHeight);
            re.Add(tmp);
        }
        return re;
    }
    private void GetMethod3BlendAndOpticalFlow(List<float[,]> downsamplePixelBlend,List<Vector2[,]> downsamplePixelOpticalFlow,int frame, int dpWidth,int dpHeight,out float[,]blend,out Vector2[,] opticalFlow) {
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        int wSeg = downsampleWidth / dpWidth;
        int hSeg = downsampleHeight / dpHeight;

        blend = new float[dpWidth, dpHeight];
        opticalFlow = new Vector2[dpWidth, dpHeight];
        //The largest salience pixel and its optical flow
        if (ifUseMaxBlendMethod3)
        {
            for (int i = 0; i < dpWidth; i++)
                for (int j = 0; j < dpHeight; j++)
                {
                    float maxBlend = 0;
                    var pos = new Vector2(i * wSeg, j * hSeg);
                    for (int ii = i * wSeg; ii < (i + 1) * wSeg; ii++)
                        for (int jj = j * hSeg; jj < (j + 1) * hSeg; jj++)
                            if (maxBlend < downsamplePixelBlend[frame][ii, jj])
                            {
                                maxBlend = downsamplePixelBlend[frame][ii, jj];
                                pos = new Vector2(ii, jj);
                            }
                    var np = pos + downsamplePixelOpticalFlow[frame][(int)pos.x, (int)pos.y];
                    NormalizePixelInRange(ref np.x, ref np.y, downsampleWidth, downsampleHeight);
                    np.x = (int)(np.x / wSeg);
                    np.y = (int)(np.y / hSeg);
                    var v = GetVector2Of2Pixels(new Vector2(i, j), np, dpWidth);
                    blend[i, j] = maxBlend;
                    opticalFlow[i, j] = v;
                }
        }
        //The optical flow of TOPK length and the largest pixel value in it
        else if (ifUseTopKOpticalFlowMethod3)
        {
            float maxBlend = 0;
            for (int i = 0; i < dpWidth; i++)
                for (int j = 0; j < dpHeight; j++)
                {
                    var listTmp = new List<Vector3>();
                    for (int ii = i * wSeg; ii < (i + 1) * wSeg; ii++)
                        for (int jj = j * hSeg; jj < (j + 1) * hSeg; jj++)
                            listTmp.Add(new Vector3(downsamplePixelOpticalFlow[frame][ii, jj].magnitude, downsamplePixelBlend[frame][ii, jj], ii * downsampleHeight + jj));
                    listTmp.Sort(new TopKComparer());
                    var dic = new Dictionary<Vector2, int>();
                    for (int k = listTmp.Count - 1; k >= listTmp.Count - k_Method3; k--)
                    {
                        int id = (int)listTmp[k].z;
                        var pos = new Vector2(id / downsampleHeight, id % downsampleHeight);
                        pos += downsamplePixelOpticalFlow[frame][(int)pos.x, (int)pos.y];
                        NormalizePixelInRange(ref pos.x, ref pos.y, downsampleWidth, downsampleHeight);
                        pos.x = (int)(pos.x / wSeg);
                        pos.y = (int)(pos.y / hSeg);
                        if (!dic.ContainsKey(pos))
                            dic[pos] = 0;
                        dic[pos]++;
                    }
                    Vector2 chosenPos = Vector2.zero;
                    int maxCnt = 0;
                    foreach (var key in dic.Keys)
                    {
                        if (dic[key] > maxCnt)
                        {
                            maxCnt = dic[key];
                            chosenPos = key;
                        }
                    }
                    float sMax = 0;
                    float sAve = 0;
                    for (int k = listTmp.Count - 1; k >= listTmp.Count - k_Method3; k--)
                    {
                        int id = (int)listTmp[k].z;
                        var pos = new Vector2(id / downsampleHeight, id % downsampleHeight);
                        pos += downsamplePixelOpticalFlow[frame][(int)pos.x, (int)pos.y];
                        NormalizePixelInRange(ref pos.x, ref pos.y, downsampleWidth, downsampleHeight);
                        pos.x = (int)(pos.x / wSeg);
                        pos.y = (int)(pos.y / hSeg);
                        if (pos.Equals(chosenPos))
                        {
                            var tmpS = downsamplePixelBlend[frame][id / downsampleHeight, id % downsampleHeight];
                            sMax = Mathf.Max(sMax, tmpS);
                            sAve += tmpS;
                        }
                    }
                    if (maxCnt != 0)
                        sAve /= maxCnt;
                    //Is the final salience the maximum or average of the same landing point
                    if (ifUseTopKOpticalFlowMaxSalMethod3)
                        blend[i, j] = sMax;
                    else
                        blend[i, j] = sAve;
                    maxBlend = Mathf.Max(maxBlend, blend[i, j]);
                    opticalFlow[i, j] = GetVector2Of2Pixels(new Vector2(i, j), chosenPos, dpWidth);
                }
            //Debug.Log(string.Format("frame:{0}, maxblend:{1}", frame, maxBlend));
        }
        //Mean optical flow and mean saliency as optical flow and saliency of coarse lattice
        else
        {
            opticalFlow = GetDownsamplePixelOpticalFlow(pixelOpticalFlow[frame], dpWidth, dpHeight, ifRoundOpticalFlow);
            float maxBlend = 0f;
            for (int i = 0; i < dpWidth; i++)
                for (int j = 0; j < dpHeight; j++)
                {
                    var s = 0f;
                    for (int ii = i * wSeg; ii < (i + 1) * wSeg; ii++)
                        for (int jj = j * hSeg; jj < (j + 1) * hSeg; jj++)
                        {
                            s += downsamplePixelBlend[frame][ii, jj];
                        }
                    s /= wSeg * hSeg;
                    blend[i, j] = s;
                    maxBlend = Mathf.Max(maxBlend, s);
                }
        }
    }
    //Rough lattice dp with equal initial path planning
    private void InitialPathPlanningMethod3()
    {
        if (pixelSaliency == null || pixelSaliency.Count == 0)
            return;
        var timeStart = DateTime.Now.Ticks;

        panelVideoController.UpdateCameraNum(panelVideoController.cameraGroupNum);
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var downsamplePixelBlend = GetDownsamplePixelBlendSaliencyMask(downsampleWidth, downsampleHeight, ifUseNormalizationPixelSaliency);
        var noNormalizationDownsamplePixelSaliency = GetDownsamplePixelSaliency(downsampleWidth, downsampleHeight, false);
        var noNormalizationDownsamplePixelMask = GetDownsamplePixelMask(downsampleWidth, downsampleHeight, false);
        var noNormalizationDownsamplePixelBlend = GetDownsamplePixelBlendSaliencyMask(noNormalizationDownsamplePixelSaliency, noNormalizationDownsamplePixelMask,downsampleWidth, downsampleHeight);
        var downsamplePixelOpticalFlow = GetDownsamplePixelOpticalFlow(downsampleWidth, downsampleHeight, true);
        int totalFrameNum = pixelSaliency.Count;

        int dpWidth = 9;
        int dpHeight = 5;
        int wSeg = downsampleWidth / dpWidth;
        int hSeg = downsampleHeight / dpHeight;

        float[] twoRectOverlapArea = new float[dpWidth * dpHeight * dpWidth * dpHeight];
        float[] singleRectArea = new float[dpWidth * dpHeight];
        for (int i = 0; i < dpWidth * dpHeight; i++)
        {
            int x = i / dpHeight;
            int y = i % dpHeight;
            x = x * wSeg + wSeg / 2;
            y = y * hSeg + hSeg / 2;
            singleRectArea[i] = GetRectWidth(rectOfPixel[x, y]) * GetRectHeight(rectOfPixel[x, y]);
            float singleArea = singleRectArea[i];
            for (int j = 0; j < dpWidth * dpHeight; j++)
            {
                int xx = j / dpHeight;
                int yy = j % dpHeight;

                xx = xx * wSeg + wSeg / 2;
                yy = yy * hSeg + hSeg / 2;
                var area = GetRectRectOverlapArea(rectOfPixel[x, y], rectOfPixel[xx, yy]);
                twoRectOverlapArea[i * dpWidth * dpHeight + j] = area;
            }
        }
        int initCamGroupNum = 1;
        int endCamGroupNum = 4;
        if (!ifAutoDetermineCamNum)
            initCamGroupNum = endCamGroupNum = panelVideoController.cameraGroupNum;

        for (int camGroupNum = initCamGroupNum; camGroupNum <= endCamGroupNum; camGroupNum++) {
            panelVideoController.UpdateCameraNum(camGroupNum);
            initialPath = new List<Vector2>[camGroupNum];

            //dp转移的半径
            int transferRadius = 1;

            var f = new List<float[]>();
            //路径
            var p = new List<int[]>();
            int statusNum = 1;
            for (int i = 0; i < camGroupNum; i++)
                statusNum *= dpWidth * dpHeight;


            Manager.DestroyTexture2dList(method3RestValuablePixelTextureList);
            method3RestValuablePixelTextureList = new List<Texture2D>();

            int threadGroupNum = 1;
            int thread_x = 5;
            int thread_y = 9;
            if (camGroupNum >= 3)
                threadGroupNum = 45;

            if (useGpu)
            {
                InitBufferMethod3(dpWidth, dpHeight, camGroupNum);
                initialPathPlanningMethod3ComputeShader.SetInt("thread_group_x", threadGroupNum);
                initialPathPlanningMethod3ComputeShader.SetInt("thread_group_y", threadGroupNum);
                initialPathPlanningMethod3ComputeShader.SetInt("dpWidth", dpWidth);
                initialPathPlanningMethod3ComputeShader.SetInt("dpHeight", dpHeight);
                initialPathPlanningMethod3ComputeShader.SetInt("camGroupNum", camGroupNum);
                initialPathPlanningMethod3ComputeShader.SetFloat("w0", w0);
                initialPathPlanningMethod3ComputeShader.SetFloat("overlapWeight", overlapWeight);
                initialPathPlanningMethod3ComputeShader.SetFloat("theta", decayTheta);
                initialPathPlanningMethod3ComputeShader.SetBool("withoutOverlapItem", withoutOverlapItem);
                twoRectOverlapAreaComputeBuffer.SetData(twoRectOverlapArea);
                singleRectAreaComputeBuffer.SetData(singleRectArea);
                Debug.Log("twoRectOverlapArea[0]: " + twoRectOverlapArea[0]);
                Debug.Log("singleRectArea[0]: " + singleRectArea[0]);
            }
            

            var blendList = new List<float[,]>();
            var opticalFlowList = new List<Vector2[,]>();

            for (int frame = 0; frame < totalFrameNum; frame++)
            {
                var opticalFlow = new Vector2[dpWidth, dpHeight];
                var blend = new float[dpWidth, dpHeight];
                GetMethod3BlendAndOpticalFlow(downsamplePixelBlend, downsamplePixelOpticalFlow, frame, dpWidth, dpHeight, out blend, out opticalFlow);
                blendList.Add(blend);
                opticalFlowList.Add(opticalFlow);
                var nowF = new float[statusNum];
                var nowP = new int[statusNum];
                if (useGpu)
                {
                    initialPathPlanningMethod3ComputeShader.SetInt("frame", frame);
                    float[] preF;
                    Vector2[,] preOpticalFlow;
                    if (frame == 0)
                    {
                        preF = new float[statusNum];
                        preOpticalFlow = new Vector2[1, 1];
                        preOpticalFlow[0, 0] = Vector2.zero;
                    }
                    else
                    {
                        preF = f[frame - 1];
                        preOpticalFlow = opticalFlowList[frame - 1];
                    }
                    preFComputeBuffer.SetData(preF);
                    downsamplePixelSaliencyComputeBuffer.SetData(blend);//The result after blend is pixelSaliency.
                    downsamplePixelOpticalFlowComputeBuffer.SetData(preOpticalFlow);
                    initialPathPlanningMethod3ComputeShader.Dispatch(kernel, threadGroupNum, threadGroupNum, statusNum / thread_x / thread_y / threadGroupNum / threadGroupNum);
                    nowFComputeBuffer.GetData(nowFDataReceiver);
                    nowPComputeBuffer.GetData(nowPDataReceiver);
                    for (int s = 0; s < statusNum; s++)
                    {
                        nowF[s] = nowFDataReceiver[s];
                        nowP[s] = nowPDataReceiver[s];
                    }
                    salEResultComputeBuffer.GetData(salEResultDataReceiver);
                }
                else
                {
                    Debug.LogError("Go and get a GPU!");
                }
                f.Add(nowF);
                p.Add(nowP);
            }
            var minF = oo;
            var chosenStatus = 0;
            var nowFrame = f.Count - 1;
            for (int i = 0; i < statusNum; i++)
                if (f[nowFrame][i] < minF)
                {
                    minF = f[nowFrame][i];
                    chosenStatus = i;
                }
            Debug.Log("minF: " + minF);
            var dpPaths = new List<Vector2>[camGroupNum];
            for (int camId = 0; camId < camGroupNum; camId++)
            {
                dpPaths[camId] = new List<Vector2>();
                initialPath[camId] = new List<Vector2>();
            }
            for (; nowFrame >= 0; nowFrame--)
            {
                var camPosList = DecodeMethod3Status(chosenStatus, camGroupNum, dpWidth, dpHeight);
                for (int camId = 0; camId < camGroupNum; camId++)
                {
                    dpPaths[camId].Add(new Vector2(camPosList[camId].x, camPosList[camId].y));
                    var realP = new Vector2(camPosList[camId].x * wSeg + wSeg / 2, camPosList[camId].y * hSeg + hSeg / 2);
                    initialPath[camId].Add(realP);
                }
                chosenStatus = p[nowFrame][chosenStatus];
            }
            for (int camId = 0; camId < camGroupNum; camId++)
            {
                dpPaths[camId].Reverse();
                initialPath[camId].Reverse();
            }
            var timeEndJointDp = DateTime.Now.Ticks;
            initPathJointDpTime = (timeEndJointDp - timeStart) / 1e7f;
            initPathRefineTime = -1;
            if (ifRefinePathMethod3)
            {                
                var timeStartRefine= DateTime.Now.Ticks;
                if (ifUseNewRefineMethod3)
                {
                    dpPaths = RefinePathMethod3(dpWidth, dpHeight, dpPaths, false);
                    dpPaths = RefinePathMethod3(2 * dpWidth, 2 * dpHeight, dpPaths, false);
                    fovAwarePath = GetMethod3RealInitialPath(downsamplePixelBlend, downsamplePixelOpticalFlow, 4 * dpWidth, 4 * dpHeight, dpPaths);
                }
                else
                {
                    fovAwarePath = GetMethod3RealInitialPath(downsamplePixelBlend, downsamplePixelOpticalFlow, dpWidth, dpHeight, dpPaths);
                }
                var timeEndRefine = DateTime.Now.Ticks;
                initPathRefineTime = (timeEndRefine - timeStartRefine) / 1e7f;
            }
            float minPs = 0;
            for (int frame = 0; frame < totalFrameNum; frame++)
            {
                float tmp = 1e9f;
                for (int camId = 0; camId < camGroupNum; camId++)
                {
                    var pix = fovAwarePath[camId][frame];
                    var s = noNormalizationDownsamplePixelBlend[frame][(int)pix.x, (int)pix.y];
                    tmp = Mathf.Min(tmp, s);
                }
                minPs += tmp;
            }
            minPs /= totalFrameNum;
            Debug.Log("minPs: " + minPs);
            if (useGpu)
            {
                ReleaseBufferMethod3();
            }
            var averagetRestValuablePixelNum = GetRestValuablePixelNum(fovAwarePath, noNormalizationDownsamplePixelBlend, downsamplePixelOpticalFlow);
            if (averagetRestValuablePixelNum < restMinimalPSNum)
                break;
        }
        var timeEnd = DateTime.Now.Ticks;
        initPathTotalTime = (timeEnd - timeStart) / 1e7f;
    }

    //FOV perception path planning
    private void FovAwarePathPlanning()
    {        
        if (initialPath == null)
            return;
        //If the initial path is calculated by greedy calculation, then initialpath stores the path before refine, and fovawarepath stores the path after refine
        if (!ifInitPathUseGreedy) {
            initialPath = fovAwarePath;
            fovAwarePath = null;
        }
        if (!ifUseFovPath)
        {
            fovAwarePath = initialPath;
            return;
        }
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var cameraGroupNum = panelVideoController.cameraGroupNum;
        var frameNum = initialPath[0].Count;
        var updateRadius = 10;//Radius used to update the path
        fovAwarePath = new List<Vector2>[cameraGroupNum];
        for (int groupId = 0; groupId < cameraGroupNum; groupId++) {
            fovAwarePath[groupId] = new List<Vector2>();
            for (int frame = 0; frame < frameNum; frame++)
            {
                var p = initialPath[groupId][frame];
                var e = oo;
                var pb = new Vector2();
                for (int i = (int)p.x - updateRadius; i <= (int)p.x + updateRadius; i++)
                    for (int j = (int)p.y - updateRadius; j <= (int)p.y + updateRadius; j++)
                    {
                        if (j < 0 || j >= downsampleHeight)
                            continue;
                        var ii = i;
                        var jj = j;
                        NormalizePixelInRange(ref ii, ref jj, downsampleWidth, downsampleHeight);
                        var pbTmp = new Vector2(ii, jj);
                        var v = GetVector2Of2Pixels(p, pbTmp, downsampleWidth);
                        var value = Mathf.Abs(1 - regionalSaliency[frame][ii, jj]) + wp * L1Norm(v);
                        if (e > value)
                        {
                            e = value;
                            pb = pbTmp;
                        }
                    }
                fovAwarePath[groupId].Add(pb);
            }
        }
    }

    //FOV perceiving path planning and recalculate regionalSaliency based on the weakened pixelSaliency.
    private void FovAwarePathPlanningMethod2()
    {
        if (initialPath == null)
            return;
        if (!ifUseFovPath) {
            fovAwarePath = initialPath;
            return;
        }
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var cameraGroupNum = panelVideoController.cameraGroupNum;
        var frameNum = initialPath[0].Count;
        var updateRadius = 10;
        fovAwarePath = new List<Vector2>[cameraGroupNum];
        everyCameraDownsampleRegionalSaliency = new List<List<float[,]>>();
        List<float> maxRegionalSaliencyEveryFrame = null;
        for (int groupId = 0; groupId < cameraGroupNum; groupId++)
        {
            var cameraRegionalSaliency = PreprocessRegionalSaliency(everyCameraDownsamplePixelSaliency[groupId], downsampleWidth, downsampleHeight, ref maxRegionalSaliencyEveryFrame, true);
            everyCameraDownsampleRegionalSaliency.Add(cameraRegionalSaliency);
            fovAwarePath[groupId] = new List<Vector2>();
            for (int frame = 0; frame < frameNum; frame++)
            {
                var p = initialPath[groupId][frame];
                var e = oo;
                var pb = new Vector2();
                for (int i = (int)p.x - updateRadius; i <= (int)p.x + updateRadius; i++)
                    for (int j = (int)p.y - updateRadius; j <= (int)p.y + updateRadius; j++)
                    {
                        if (j < 0 || j >= downsampleHeight)
                            continue;
                        var ii = i;
                        var jj = j;
                        NormalizePixelInRange(ref ii, ref jj, downsampleWidth, downsampleHeight);
                        var pbTmp = new Vector2(ii, jj);
                        var v = GetVector2Of2Pixels(p, pbTmp, downsampleWidth);
                        var value = Mathf.Abs(1 - cameraRegionalSaliency[frame][ii, jj]) + wp * L1Norm(v);
                        if (e > value)
                        {
                            e = value;
                            pb = pbTmp;
                        }
                    }
                fovAwarePath[groupId].Add(pb);
            }
        }
    }

    private void PathSmoothing()
    {
        if (fovAwarePath == null)
            return;
        var timeStart = DateTime.Now.Ticks;

        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var updateRadius = 5;
        var updateWidth = 2 * updateRadius + 1;
        var wv = 2e2f;
        var wa = 2e4f;        
        var cameraGroupNum = panelVideoController.cameraGroupNum;
        var frameNum = fovAwarePath[0].Count;

        smoothPath = new List<Vector2>[cameraGroupNum];
        for (int groupId = 0; groupId < cameraGroupNum; groupId++) {
            var f = new List<float[,]>();
            var p = new List<Vector2[,]>();

            for (int frame = 0; frame < frameNum; frame++)
            {
                var nowF = new float[downsampleWidth * downsampleHeight, updateWidth * updateWidth];
                var nowP = new Vector2[downsampleWidth * downsampleHeight, updateWidth * updateWidth];
                var pb = fovAwarePath[groupId][frame];
                for (int i = 0; i < downsampleWidth * downsampleHeight; i++)
                {
                    var x = i % downsampleWidth;
                    var y = i / downsampleWidth;
                    var pj = new Vector2(x, y);
                    for (int j = 0; j < updateWidth * updateWidth; j++)
                    {
                        nowF[i, j] = oo;
                        var x1 = x - updateRadius + j % updateWidth;
                        var y1 = y - updateRadius + j / updateWidth;
                        if (y1 < 0 || y1 >= downsampleHeight)
                            continue;
                        NormalizePixelInRange(ref x1, ref y1, downsampleWidth, downsampleHeight);
                        var pj1 = new Vector2(x1, y1);
                        if (frame == 0)
                        {
                            if (pj1 == pj)
                                nowF[i, j] = (pj - pb).sqrMagnitude;
                            continue;
                        }
                        var preF = f[f.Count - 1];
                        for (int k = 0; k < updateWidth * updateWidth; k++)
                        {
                            var x2 = x1 - updateRadius + k % updateWidth;
                            var y2 = y1 - updateRadius + k / updateWidth;
                            if (y2 < 0 || y2 >= downsampleHeight)
                                continue;
                            NormalizePixelInRange(ref x2, ref y2, downsampleWidth, downsampleHeight);
                            var pj2 = new Vector2(x2, y2);

                            var value = GetVector2Of2Pixels(pb, pj, downsampleWidth).sqrMagnitude + wv * GetVector2Of2Pixels(pj1, pj, downsampleWidth).sqrMagnitude
                                + wa * (GetVector2Of2Pixels(pj1, pj, downsampleWidth) - GetVector2Of2Pixels(pj2, pj1, downsampleWidth)).sqrMagnitude + preF[y1 * downsampleWidth + x1, k];
                            if (nowF[i, j] > value)
                            {
                                nowF[i, j] = value;
                                nowP[i, j] = new Vector2(y1 * downsampleWidth + x1, k);
                            }
                        }
                    }
                }
                f.Add(nowF);
                p.Add(nowP);
            }
            var minF = oo;
            var id = f.Count - 1;
            var nowState = new Vector2();
            for (int i = 0; i < downsampleWidth * downsampleHeight; i++)
                for (int j = 0; j < updateWidth * updateWidth; j++)
                    if (minF > f[id][i, j])
                    {
                        minF = f[id][i, j];
                        nowState = new Vector2(i, j);
                    }
            while (id >= 0)
            {
                var st = (int)nowState.x;
                var nowPixel = new Vector2(st % downsampleWidth, st / downsampleWidth);
                smoothPath[groupId].Add(nowPixel);
                nowState = p[id][(int)nowState.x, (int)nowState.y];
                id--;
            }
            smoothPath[groupId].Reverse();
            Debug.Log("minF: " + minF);
        }

        var timeEnd = DateTime.Now.Ticks;
        smoothPathTime = (timeEnd - timeStart) / 1e7f;
    }

    private void InitBuffer() {
        int downsampleWidth, downsampleHeight;
        GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        kernel = initialPathPlanningComputeShader.FindKernel("CSMain");
        preFComputeBuffer = new ComputeBuffer(downsampleWidth * downsampleHeight, 4);
        downsamplePixelSaliencyComputeBuffer = new ComputeBuffer(downsampleWidth * downsampleHeight, 4);
        downsamplePixelOpticalFlowComputeBuffer = new ComputeBuffer(downsampleWidth * downsampleHeight, 8);
        nowFComputeBuffer = new ComputeBuffer(downsampleWidth * downsampleHeight, 4);
        nowPComputeBuffer = new ComputeBuffer(downsampleWidth * downsampleHeight, 4);
        initialPathPlanningComputeShader.SetBuffer(kernel, "preF", preFComputeBuffer);
        initialPathPlanningComputeShader.SetBuffer(kernel, "downsamplePixelSaliency", downsamplePixelSaliencyComputeBuffer);
        initialPathPlanningComputeShader.SetBuffer(kernel, "downsamplePixelOpticalFlow", downsamplePixelOpticalFlowComputeBuffer);
        initialPathPlanningComputeShader.SetBuffer(kernel, "nowF", nowFComputeBuffer);
        initialPathPlanningComputeShader.SetBuffer(kernel, "nowP", nowPComputeBuffer);
        nowFDataReceiver = new float[downsampleWidth * downsampleHeight];
        nowPDataReceiver = new int[downsampleWidth * downsampleHeight];
    }
    private void ReleaseBuffer()
    {
        preFComputeBuffer.Release();
        downsamplePixelSaliencyComputeBuffer.Release();
        downsamplePixelOpticalFlowComputeBuffer.Release();
        nowFComputeBuffer.Release();
        nowPComputeBuffer.Release();
    }

    private void InitBufferMethod3(int dpWidth, int dpHeight, int camGroupNum)
    {
        kernel = initialPathPlanningMethod3ComputeShader.FindKernel("CSMain");
        int statusNum = 1;
        for (int i = 0; i < camGroupNum; i++)
            statusNum *= dpWidth * dpHeight;
        twoRectOverlapAreaComputeBuffer = new ComputeBuffer(dpWidth * dpHeight * dpWidth * dpHeight, 4);
        singleRectAreaComputeBuffer = new ComputeBuffer(dpWidth * dpHeight, 4);
        preFComputeBuffer = new ComputeBuffer(statusNum, 4);
        downsamplePixelSaliencyComputeBuffer = new ComputeBuffer(dpWidth * dpHeight, 4);
        downsamplePixelOpticalFlowComputeBuffer = new ComputeBuffer(dpWidth * dpHeight, 8);
        nowFComputeBuffer = new ComputeBuffer(statusNum, 4);
        nowPComputeBuffer = new ComputeBuffer(statusNum, 4);
        initialPathPlanningMethod3ComputeShader.SetBuffer(kernel, "twoRectOverlapArea", twoRectOverlapAreaComputeBuffer);
        initialPathPlanningMethod3ComputeShader.SetBuffer(kernel, "singleRectArea", singleRectAreaComputeBuffer);
        initialPathPlanningMethod3ComputeShader.SetBuffer(kernel, "preF", preFComputeBuffer);
        initialPathPlanningMethod3ComputeShader.SetBuffer(kernel, "downsamplePixelSaliency", downsamplePixelSaliencyComputeBuffer);
        initialPathPlanningMethod3ComputeShader.SetBuffer(kernel, "downsamplePixelOpticalFlow", downsamplePixelOpticalFlowComputeBuffer);
        initialPathPlanningMethod3ComputeShader.SetBuffer(kernel, "nowF", nowFComputeBuffer);
        initialPathPlanningMethod3ComputeShader.SetBuffer(kernel, "nowP", nowPComputeBuffer);
        nowFDataReceiver = new float[statusNum];
        nowPDataReceiver = new int[statusNum];

        salEResultComputeBuffer = new ComputeBuffer(statusNum, 4);
        initialPathPlanningMethod3ComputeShader.SetBuffer(kernel, "salEResult", salEResultComputeBuffer);
        salEResultDataReceiver = new float[statusNum];
    }
    private void ReleaseBufferMethod3()
    {
        twoRectOverlapAreaComputeBuffer.Release();
        singleRectAreaComputeBuffer.Release();
        preFComputeBuffer.Release();
        downsamplePixelSaliencyComputeBuffer.Release();
        downsamplePixelOpticalFlowComputeBuffer.Release();
        nowFComputeBuffer.Release();
        nowPComputeBuffer.Release();
    }

    public void LoadCameraPaths3(string path) {
        var content = File.ReadAllText(path);
        var recordCameraPaths = RecordCameraPaths.CreateFromJSON(content);
        initialPath = recordCameraPaths.GetInitialPath();
        fovAwarePath = recordCameraPaths.GetFovAwarePath();
        smoothPath = recordCameraPaths.GetSmoothPath();
        panelVideoController.UpdateCameraNum(smoothPath.Length);
        var sceneNumber = Manager.GetActivateSceneNumber();
        if (sceneNumber == 0)
            panelVideoController.PrepareCameraOpticalFlowWindowTextures();
        if (Manager.GetActivateSceneNumber() == 1)
        {
            manager.mainNFOVController.PrepareIfCloseCameraWindow();
        }
    }

    public void SaveCameraPaths(string path) {
        var recordCameraPaths = new RecordCameraPaths();
        var a = initialPath;
        var b = fovAwarePath;
        var c = smoothPath;
        recordCameraPaths.SetPaths(a, b, c);
        var content = recordCameraPaths.SaveToString();
        File.WriteAllText(path, content);
    }


    [DllImport("testCeres_Array")]
    public static extern int GetSmoothPath(float[] fovPathData, int cameraNum, int frameNum);


    //Calculate the path
    public void PreparePath()
    {
        preparePathTotalTime = -1;
        initPathTotalTime = -1;
        initPathJointDpTime = -1;
        initPathRefineTime = -1;
        renderCamWindowTime = -1;
        smoothPathTime = -1;
        var timeStart = DateTime.Now.Ticks;
        initialPath = null;
        fovAwarePath = null;
        smoothPath = null;
        Manager.DestroyTexture2dList(panelVideoController.everyCameraPixelSaliencyTextureList);
        Manager.DestroyTexture2dList(panelVideoController.everyCameraRegionalSaliencyTextureList);
        everyCameraDownsamplePixelSaliency = null;
        everyCameraDownsampleRegionalSaliency = null;
        if (ifInitPathUseGreedy)
            InitialPathPlanning();
        else
            InitialPathPlanningMethod3();//Now we replace the position of fovpath with initialpath after refine
        var timeInitialEnd = DateTime.Now.Ticks;
        Debug.Log("InitialPathPlanning timeCost: " + (timeInitialEnd - timeStart) / 1e7);

        var timeFovAwareStart = DateTime.Now.Ticks;
        if (ifUseFovPath)
            FovAwarePathPlanning();
        var timeFovAwareEnd = DateTime.Now.Ticks;
        Debug.Log("FovAwarePathPlanning timeCost: " + (timeFovAwareEnd - timeFovAwareStart) / 1e7);

        var timeSmoothStart = DateTime.Now.Ticks;
        var timeSmoothEnd = DateTime.Now.Ticks;
        Debug.Log("PathSmoothing timeCost: " + (timeSmoothEnd - timeSmoothStart) / 1e7);
        
        if (!Directory.Exists(resultDir)) {
            try
            {
                Directory.CreateDirectory(resultDir);
            }
            catch {
                Debug.Log("Output Path Result Fail!");
            }            
        }
        var pathCamera2 = resultDir + "\\" + "cp2.json";
        var pathCamera3 = resultDir + "\\" + "cp3.json";
        SaveCameraPaths(pathCamera2);
        int camNum = panelVideoController.cameraGroupNum;
        int frameNum = initialPath[0].Count;
        float[] fovPathData = new float[camNum * frameNum * 2];
        for (int camId = 0; camId < camNum; camId++)
            for (int frame = 0; frame < frameNum; frame++) {
                fovPathData[camId * frameNum * 2 + frame] = fovAwarePath[camId][frame].x;
                fovPathData[camId * frameNum * 2 + frameNum + frame] = fovAwarePath[camId][frame].y;
            }
        var timeStartSmoothPath = DateTime.Now.Ticks;
        if (GetSmoothPath(fovPathData, camNum, frameNum) == 0)
        {
            var timeEndSmoothPath = DateTime.Now.Ticks;            
            smoothPathTime = (timeEndSmoothPath - timeStartSmoothPath) / 1e7f;
            Debug.Log("GetSmoothPath timeCost: " + smoothPathTime);
            smoothPath = new List<Vector2>[camNum];
            for (int camId = 0; camId < camNum; camId++) {
                smoothPath[camId] = new List<Vector2>();
                for (int frame = 0; frame < frameNum; frame++)
                {
                    smoothPath[camId].Add(new Vector2(fovPathData[camId * frameNum * 2 + frame], fovPathData[camId * frameNum * 2 + frameNum + frame]));
                }
            }
            Debug.Log(string.Format("camNum:{0}, frameNum:{1}, fovPathData.Length: {2} ", camNum, frameNum, fovPathData.Length));
            Debug.Log(string.Format("smoothPath.length:{0}, smoothPath[0].count", smoothPath.Length, smoothPath[0].Count));
            SaveCameraPaths(pathCamera3);
        }
        else {
            Debug.Log("GetSmoothPath fail!");
        }
        panelVideoController.UpdateCameraNum(initialPath.Length);        
        panelVideoController.PrepareCameraOpticalFlowWindowTextures();
        UpdateEveryCameraPixelSaliencyTextureList();
        UpdateMethod3PixelSaliencyAndOpticalFlowTextureList();
        panelVideoController.PrepareEveryCameraRegionalSaliencyTextures();
        var timeEnd = DateTime.Now.Ticks;
        preparePathTotalTime = (timeEnd - timeStart) / 1e7f;
        Debug.Log("PreparePath timeCost: " + preparePathTotalTime);
    }
    private void OnDestroy()
    {
        Debug.Log("opticalFlowCamerasController.OnDestroy");
        Manager.DestroyTexture2dList(method3OpticalFlowTextureList);
        Manager.DestroyTexture2dList(method3PixelSaliencyTextureList);
        Manager.DestroyTexture2dList(method3RestValuablePixelTextureList);
    }
}
