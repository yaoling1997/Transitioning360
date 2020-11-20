using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using System.Runtime.InteropServices;

public class KoreaMethod : MonoBehaviour
{    
    private readonly static int maxStoredKeyFrameNum = 100;
    private readonly static float oo = 1e9f;
    private Manager manager;
    private OpticalFlowCamerasController opticalFlowCamerasController;
    private PanelVideoController panelVideoController;
    public Camera mainCamera;
    private VideoPlayer videoPlayer;        

    private int currentKeyFrame;
    private Vector2 currentPos;

    private float w0 = 0.03f;
    private int updateRadius = 15;//Used to update the status of the radius                            

    public int initKeyFrameNum = 4;
    public int downsampleWidth;
    public int downsampleHeight;
    public List<float[,]> downsamplePixelBlend;
    public List<Vector2[,]> downsamplePixelOpticalFlow;
    public List<float[,]> regionalSaliency;

    public int nowStoredKeyFrameNum = 0;
    public int nowBeginKeyFrame = 0;
    public List<Vector2> nowSmoothPath;//Smoothed path

    public int nextStoredKeyFrameNum = 0;
    public int nextBeginKeyFrame = 0;
    public List<Vector2> nextSmoothPath;//Smoothed path

    public int ifGetSmoothPath = 0;

    public int initialPathKeyFrameNumPerFrame;//How many initialpathkeyframe is calculated at most per frame
    public int fovPathKeyFrameNumPerFrame;//How much fovpathkeyframe is calculated at most per frame

    public float maxSpeed;//How many pixels can be moved at most per second

    public ComputeShader koreaInitialPathPlanningComputeShader;
    //Buffer for ComputeShader
    private ComputeBuffer preFComputeBuffer;
    private ComputeBuffer downsamplePixelSaliencyComputeBuffer;
    private ComputeBuffer downsamplePixelOpticalFlowComputeBuffer;
    private ComputeBuffer nowFComputeBuffer;
    private ComputeBuffer nowPComputeBuffer;

    private Coroutine preparePathCoroutine;
    private Coroutine initialPathCoroutine;
    private Coroutine fovAwarePathCoroutine;

    //Get data from buffer
    private float[] nowFDataReceiver;
    private int[] nowPDataReceiver;

    int kernel;
    public bool threadIsAlive = false;
    public bool isReady;//Is initialization called

    private void Awake()
    {
        manager = GameObject.Find("Manager").GetComponent<Manager>();
        opticalFlowCamerasController = manager.opticalFlowCamerasController;
        panelVideoController = manager.panelVideoController;
        videoPlayer = manager.videoPlayer;
        initialPathKeyFrameNumPerFrame = 10;
        fovPathKeyFrameNumPerFrame = 10;
        maxSpeed = 30;
        isReady = false;
    }
    public void Init()
    {
        Clear();
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        downsamplePixelBlend = opticalFlowCamerasController.downsamplePixelBlend;
        downsamplePixelOpticalFlow = opticalFlowCamerasController.downsamplePixelOpticalFlow;
        regionalSaliency = opticalFlowCamerasController.downsampleRegionalSaliency;
        InitBuffer();//It only needs to be initialized once
        InitPath();
        isReady = true;
        StartNewThread();
    }
    public void Clear() {
        StopThread();
        isReady = false;
    }
    private void Update()
    {
        UpdateMainCamera();        
    }

    public void UpdateMainCamera()
    {
        if (videoPlayer.isPlaying == false || !isReady)
            return;
        //When Korea + multimethod is used, the Korea method will not be enabled when it enters the following sub camera state
        if (manager.panoramaVideoController.IfUseKoreaPlusMultiMethod() && manager.panoramaVideoController.multiMethod.IfFollowMode())
            return;
        var currentFrame = (int)videoPlayer.frame;
        //Debug.Log("UpdateMainCamera.currentFrame_begin: "+ currentFrame);
        var currentKeyFrame = GetCurrentKeyFrame();
        var totalKeyframe = downsamplePixelBlend.Count;
        //The thread can not be turned until it is finished
        if (nextBeginKeyFrame <= Mathf.Min(currentKeyFrame, totalKeyframe - 2) && !IfThreadAlive())
        {
            //Debug.Log("K.nextBeginKeyFrame <= currentKeyFrame");            
            nowBeginKeyFrame = nextBeginKeyFrame;
            nowStoredKeyFrameNum = nextStoredKeyFrameNum;
            nowSmoothPath = nextSmoothPath;
            StartNewThread();
        }

        if (nowBeginKeyFrame <= currentKeyFrame && currentKeyFrame < Mathf.Min(nowBeginKeyFrame + nowStoredKeyFrameNum - 1, totalKeyframe))
        {
            var w = RectangleInterpolation(currentKeyFrame, currentFrame);
            w = ApplyMaxSpeedLimit(mainCamera.transform.forward, w, Time.deltaTime);
            mainCamera.transform.forward = w;
            manager.panoramaVideoController.UpdateTextMethodStatus(1);
            mainCamera.Render();            
        }
    }
    //Limit the maximum moving speed to prevent the window from shaking too much
    public Vector3 ApplyMaxSpeedLimit(Vector3 fa,Vector3 fb,float t) {
        if (fa.Equals(fb))
            return fa;
        var originSize = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var cameraCalculate = manager.cameraCalculate;
        cameraCalculate.transform.forward = fa;
        var ea = cameraCalculate.transform.eulerAngles;
        var pa = panelVideoController.EulerAngleToPixel(ea);
        pa = opticalFlowCamerasController.PixelToOriginPixelFloatValue(pa, originSize, new Vector2(downsampleWidth, downsampleHeight));
        cameraCalculate.transform.forward = fb;
        var eb = cameraCalculate.transform.eulerAngles;
        var pb = panelVideoController.EulerAngleToPixel(eb);
        pb = opticalFlowCamerasController.PixelToOriginPixelFloatValue(pb, originSize, new Vector2(downsampleWidth, downsampleHeight));
        var v = opticalFlowCamerasController.GetVector2Of2Pixels(pa, pb, downsampleWidth);
        if (v.magnitude==0)
            return fa;
        v = v / v.magnitude * Mathf.Min(maxSpeed * t, v.magnitude);
        Debug.Log("ApplyMaxSpeedLimit");
        Debug.Log(string.Format("t:{0}, v: {1}", t, v));
        var p = pa + v;
        OpticalFlowCamerasController.NormalizePixelInRange(ref p.x, ref p.y, downsampleWidth, downsampleHeight);
        return panelVideoController.PixelToVector3(opticalFlowCamerasController.PixelToOriginPixelFloatValue(p, new Vector2(downsampleWidth, downsampleHeight), originSize));
    }
    //Interpolation on a rectangle
    public Vector3 RectangleInterpolation(int currentKeyFrame, int currentFrame) {        
        var totalKeyframe = downsamplePixelBlend.Count;
        var l = currentKeyFrame - nowBeginKeyFrame;
        var r = l + 1;
        var chosenPath = nowSmoothPath;
        var originSize = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var al = chosenPath[l];
        var tl = (float)(l + nowBeginKeyFrame) / totalKeyframe;
        var w = Vector3.zero;
        if (r == chosenPath.Count)//Only the last camera is left
        {
            w = panelVideoController.PixelToVector3(opticalFlowCamerasController.PixelToOriginPixelIntValue(chosenPath[l], new Vector2(downsampleWidth, downsampleHeight), originSize));
        }
        else {
            var ar = chosenPath[r];
            var tr = (float)(r + nowBeginKeyFrame) / totalKeyframe;
            var v = opticalFlowCamerasController.GetVector2Of2Pixels(al, ar, downsampleWidth);            
            var percentage = (float)currentFrame / videoPlayer.frameCount;
            var tmp = (percentage - tl) / (tr - tl);
            tmp = Mathf.Clamp(tmp, 0, 1);            
            v = v * tmp;
            var p = al + v;
            OpticalFlowCamerasController.NormalizePixelInRange(ref p.x, ref p.y, downsampleWidth, downsampleHeight);
            var op = opticalFlowCamerasController.PixelToOriginPixelFloatValue(p, new Vector2(downsampleWidth, downsampleHeight), originSize);
            w = panelVideoController.PixelToVector3(op);
        }
        return w;
    }
    //Orientation of spherical interpolation
    public Vector3 SphereInterpolation(int currentKeyFrame,int currentFrame)
    {
        var totalKeyframe = downsamplePixelBlend.Count;
        var l = currentKeyFrame - nowBeginKeyFrame;
        var r = l + 1;
        var chosenPath = nowSmoothPath;
        var originSize = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var al = panelVideoController.PixelToAngle(opticalFlowCamerasController.PixelToOriginPixelIntValue(chosenPath[l], new Vector2(downsampleWidth, downsampleHeight), originSize));
        var tl = (float)(l + nowBeginKeyFrame) / totalKeyframe;
        var w = Vector3.zero;
        if (r == chosenPath.Count)//Only the view of the last camera is left

        {
            w = panelVideoController.PixelToVector3(opticalFlowCamerasController.PixelToOriginPixelIntValue(chosenPath[l], new Vector2(downsampleWidth, downsampleHeight), originSize));
        }
        else{
            var ar = panelVideoController.PixelToAngle(opticalFlowCamerasController.PixelToOriginPixelIntValue(chosenPath[r], new Vector2(downsampleWidth, downsampleHeight), originSize));
            var tr = (float)(r + nowBeginKeyFrame) / totalKeyframe;
            var u = panelVideoController.EulerAngleToVector3(panelVideoController.AngleToEulerAngle(al));
            var v = panelVideoController.EulerAngleToVector3(panelVideoController.AngleToEulerAngle(ar));
            var percentage = (float)currentFrame / videoPlayer.frameCount;
            var tmp = (percentage - tl) / (tr - tl);
            tmp = Mathf.Clamp(tmp, 0, 1);
            var theta = Vector3.Angle(u, v) * tmp * Mathf.Deg2Rad;
            w = Vector3.RotateTowards(u, v, theta, 1);
        }
        return w;
    }
    
    public void StartNewThread()
    {
        if (IfThreadAlive())
        {
            StopThread();
        }
        //When Korea + multimethod is used, the Korea method will not be enabled when it enters the following sub camera state
        if (manager.panoramaVideoController.IfUseKoreaPlusMultiMethod() && manager.panoramaVideoController.multiMethod.IfFollowMode())
            return;        
        var currentKeyFrame = GetCurrentKeyFrame();
        var originPixel = panelVideoController.EulerAngleToPixel(mainCamera.transform.eulerAngles);
        var originSize = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var tmpPos = opticalFlowCamerasController.PixelToOriginPixelFloatValue(originPixel, originSize, new Vector2(downsampleWidth, downsampleHeight));
        var currentPos = new Vector2((int)tmpPos.x, (int)tmpPos.y);        

        StartThreadToGetNextPath(currentKeyFrame, currentPos);
    }

    //Is the thread executing
    public bool IfThreadAlive()
    {
        return isReady && threadIsAlive;
    }

    //Clear current path
    public void InitPath()
    {
        nowSmoothPath = null;
        nowStoredKeyFrameNum = 0;
        nowBeginKeyFrame = 0;

        nextSmoothPath = null;
        nextStoredKeyFrameNum = 0;
        nextBeginKeyFrame = 0;
        Debug.Log("InitPath");
    }
    
    public void InitKoreaMethodPaths()
    {
        if (IfThreadAlive())
            StopThread();
        if (!isReady)
            return;
        InitPath();
        StartNewThread();
    }

    //Get current keyframe
    public int GetCurrentKeyFrame()
    {
        if (opticalFlowCamerasController.pixelSaliency == null)
        {
            return 0;
        }
        int totalKeyFrame = opticalFlowCamerasController.pixelSaliency.Count;
        return (int)(Mathf.Min(videoPlayer.frame, videoPlayer.frameCount - 1) * totalKeyFrame / videoPlayer.frameCount);
    }

    private static int Max(int a, int b)
    {
        return a > b ? a : b;
    }
    private static int Min(int a, int b)
    {
        return a < b ? a : b;
    }

    //Buffer used to initialize GPU
    private void InitBuffer()
    {
        kernel = koreaInitialPathPlanningComputeShader.FindKernel("CSMain");
        preFComputeBuffer = new ComputeBuffer(downsampleWidth * downsampleHeight, 4);
        downsamplePixelSaliencyComputeBuffer = new ComputeBuffer(downsampleWidth * downsampleHeight, 4);
        downsamplePixelOpticalFlowComputeBuffer = new ComputeBuffer(downsampleWidth * downsampleHeight, 8);
        nowFComputeBuffer = new ComputeBuffer(downsampleWidth * downsampleHeight, 4);
        nowPComputeBuffer = new ComputeBuffer(downsampleWidth * downsampleHeight, 4);
        koreaInitialPathPlanningComputeShader.SetBuffer(kernel, "preF", preFComputeBuffer);
        koreaInitialPathPlanningComputeShader.SetBuffer(kernel, "downsamplePixelSaliency", downsamplePixelSaliencyComputeBuffer);
        koreaInitialPathPlanningComputeShader.SetBuffer(kernel, "downsamplePixelOpticalFlow", downsamplePixelOpticalFlowComputeBuffer);
        koreaInitialPathPlanningComputeShader.SetBuffer(kernel, "nowF", nowFComputeBuffer);
        koreaInitialPathPlanningComputeShader.SetBuffer(kernel, "nowP", nowPComputeBuffer);
        nowFDataReceiver = new float[downsampleWidth * downsampleHeight];
        nowPDataReceiver = new int[downsampleWidth * downsampleHeight];
    }
    //Release the buffer used by GPU
    private void ReleaseBuffer()
    {
        preFComputeBuffer.Release();
        downsamplePixelSaliencyComputeBuffer.Release();
        downsamplePixelOpticalFlowComputeBuffer.Release();
        nowFComputeBuffer.Release();
        nowPComputeBuffer.Release();
    }

    //Initial path planning multi thread call
    //Start keyframe length start position (180 * 101)
    private IEnumerator InitialPathPlanning(int beginKeyFrame, int keyFrameLength, Vector2 beginPos, List<Vector2> initialPath)
    {
        if (downsamplePixelBlend != null && downsamplePixelBlend.Count != 0)
        {
            koreaInitialPathPlanningComputeShader.SetInt("downsampleWidth", downsampleWidth);
            koreaInitialPathPlanningComputeShader.SetInt("downsampleHeight", downsampleHeight);
            koreaInitialPathPlanningComputeShader.SetInt("updateRadius", updateRadius);
            koreaInitialPathPlanningComputeShader.SetFloat("w0", w0);

            var f = new List<float[,]>();
            var p = new List<Vector2[,]>();
            var nowF = new float[downsampleWidth, downsampleHeight];
            var nowP = new Vector2[downsampleWidth, downsampleHeight];
            //The starting position is a legal position
            for (int i = 0; i < downsampleWidth; i++)
                for (int j = 0; j < downsampleHeight; j++)
                    if (i == beginPos.x && j == beginPos.y)
                        nowF[i, j] = Mathf.Abs(1 - downsamplePixelBlend[beginKeyFrame][i, j]);
                    else
                        nowF[i, j] = oo;
            f.Add(nowF);
            p.Add(nowP);
            int cntFrame = 0;
            for (int frame = beginKeyFrame + 1; frame < beginKeyFrame + keyFrameLength; frame++)
            {
                var preF = f[f.Count - 1];
                nowF = new float[downsampleWidth, downsampleHeight];
                nowP = new Vector2[downsampleWidth, downsampleHeight];
                preFComputeBuffer.SetData(preF);
                downsamplePixelSaliencyComputeBuffer.SetData(downsamplePixelBlend[frame]);
                downsamplePixelOpticalFlowComputeBuffer.SetData(downsamplePixelOpticalFlow[frame - 1]);

                koreaInitialPathPlanningComputeShader.Dispatch(kernel, 1, 1, downsampleWidth * downsampleHeight / 6 / 6);
                nowFComputeBuffer.GetData(nowFDataReceiver);
                nowPComputeBuffer.GetData(nowPDataReceiver);
                for (int s = 0; s < downsampleWidth * downsampleHeight; s++)
                {
                    int i = s / downsampleHeight;
                    int j = s % downsampleHeight;
                    int preS = nowPDataReceiver[s];
                    nowF[i, j] = nowFDataReceiver[s];
                    nowP[i, j] = new Vector2(preS / downsampleHeight, preS % downsampleHeight);
                }

                f.Add(nowF);
                p.Add(nowP);
                cntFrame++;
                if (cntFrame >= initialPathKeyFrameNumPerFrame)
                {
                    cntFrame = 0;
                    yield return null;
                }
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
                initialPath.Add(nowPixel);
                nowPixel = p[id][(int)nowPixel.x, (int)nowPixel.y];
                id--;
            }
            initialPath.Reverse();
        }
        yield return null;
    }

    //FOV perception path planning
    private IEnumerator FovAwarePathPlanning(List<Vector2> initialPath, int nextBeginKeyFrame, int nextStoredKeyFrameNum, List<Vector2> fovAwarePath)
    {
        if (initialPath != null&& initialPath.Count>0)
        {
            var frameNum = initialPath.Count;
            var updateRadius = 10;//Radius used to update the path   
            var wp = 1e-4f;            
            fovAwarePath.Add(initialPath[0]);
            int cntFrame = 0;
            for (int frame = nextBeginKeyFrame + 1; frame < nextBeginKeyFrame + nextStoredKeyFrameNum; frame++)
            {
                var p = initialPath[frame - nextBeginKeyFrame];
                var e = oo;
                var pb = new Vector2();
                for (int i = (int)p.x - updateRadius; i <= (int)p.x + updateRadius; i++)
                    for (int j = (int)p.y - updateRadius; j <= (int)p.y + updateRadius; j++)
                    {
                        if (j < 0 || j >= downsampleHeight)
                            continue;
                        var ii = i;
                        var jj = j;
                        OpticalFlowCamerasController.NormalizePixelInRange(ref ii, ref jj, downsampleWidth, downsampleHeight);
                        var pbTmp = new Vector2(ii, jj);
                        var v = opticalFlowCamerasController.GetVector2Of2Pixels(p, pbTmp, downsampleWidth);
                        float value = 0;
                        try
                        {
                            value = Mathf.Abs(1 - regionalSaliency[frame][ii, jj]) + wp * opticalFlowCamerasController.L1Norm(v);
                        }
                        catch {
                            Debug.LogError(string.Format("error! FovAwarePathPlanning regionalSaliency,regionalSaliency.count:{0},frame:{1}", regionalSaliency.Count, frame));
                        }                        
                        if (e > value)
                        {
                            e = value;
                            pb = pbTmp;
                        }
                    }
                fovAwarePath.Add(pb);
                cntFrame++;
                if (cntFrame >= fovPathKeyFrameNumPerFrame)
                {
                    cntFrame = 0;
                    yield return null;
                }
            }
        }
        yield return null;
    }

    //The path for multithreading to get the next set of key frames
    public void StartThreadToGetNextPath(int _currentKeyFrame, Vector2 _currentPos)
    {
        currentKeyFrame = _currentKeyFrame;
        currentPos = _currentPos;
        preparePathCoroutine = StartCoroutine(PreparePath());
    }
    public void StopThread()
    {
        if (IfThreadAlive())
        {
            if (preparePathCoroutine!=null)
                StopCoroutine(preparePathCoroutine);
            if (initialPathCoroutine!=null)
                StopCoroutine(initialPathCoroutine);
            if (fovAwarePathCoroutine != null)
                StopCoroutine(fovAwarePathCoroutine);
        }
    }
    [DllImport("testCeres_Array")]
    public static extern int GetSmoothPath(float[] fovPathData, int cameraNum, int frameNum);

    
    public IEnumerator PreparePath()
    {        
        Debug.Log("koreaMethod.PreparePath");
        if (isReady) {
            threadIsAlive = true;
            if (downsamplePixelBlend != null)
            {
                if (nextStoredKeyFrameNum == 0)
                    nextStoredKeyFrameNum = initKeyFrameNum;
                else
                {
                    nextStoredKeyFrameNum = Min(2 * nextStoredKeyFrameNum, maxStoredKeyFrameNum);
                }

                var nextInitialPath = new List<Vector2>();
                var nextFovAwarePath = new List<Vector2>();
                Vector2 beginPos = Vector2.zero;
                if (nowSmoothPath == null)
                {
                    beginPos = currentPos;
                    nextBeginKeyFrame = currentKeyFrame + nextStoredKeyFrameNum;
                }
                else
                {
                    beginPos = nowSmoothPath[nowSmoothPath.Count - 1];
                    nextBeginKeyFrame = nowBeginKeyFrame + nowStoredKeyFrameNum - 1;
                }
                int totalKeyFrame = downsamplePixelBlend.Count;
                if (nextBeginKeyFrame < totalKeyFrame - 1)
                {
                    nextStoredKeyFrameNum = Mathf.Min(nextStoredKeyFrameNum, totalKeyFrame - nextBeginKeyFrame);
                    initialPathCoroutine = StartCoroutine(InitialPathPlanning(nextBeginKeyFrame, nextStoredKeyFrameNum, beginPos, nextInitialPath));
                    yield return null;
                    ifGetSmoothPath = 3;
                    fovAwarePathCoroutine = StartCoroutine(FovAwarePathPlanning(nextInitialPath, nextBeginKeyFrame, nextStoredKeyFrameNum, nextFovAwarePath));
                    yield return null;
                    ifGetSmoothPath = 4;
                    int frameNum = nextFovAwarePath.Count;
                    float[] fovPathData = new float[frameNum * 2];
                    for (int frame = 0; frame < frameNum; frame++)
                    {
                        fovPathData[frame] = nextFovAwarePath[frame].x;
                        fovPathData[frameNum + frame] = nextFovAwarePath[frame].y;
                    }
                    if (GetSmoothPath(fovPathData, 1, frameNum) == 0)
                    {
                        ifGetSmoothPath = 5;
                        nextSmoothPath = new List<Vector2>();
                        for (int frame = 0; frame < frameNum; frame++)
                        {
                            nextSmoothPath.Add(new Vector2(Mathf.Round(fovPathData[frame]), Mathf.Round(fovPathData[frameNum + frame])));
                        }
                        if (nextSmoothPath.Count>0)
                            nextSmoothPath[0] = beginPos;//The first frame should correspond to the previous one, otherwise the interpolation is not smooth enough
                    }
                    else
                    {
                        Debug.Log("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG");
                    }
                }
            }
            yield return null;
            threadIsAlive = false;
        }
    }
}

