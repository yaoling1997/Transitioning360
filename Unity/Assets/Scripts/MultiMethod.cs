using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class MultiMethod : MonoBehaviour {
    private static readonly float initArrowShowTime = 1;
    private Manager manager;
    private RawImage mainNFOV23Content;
    private int nowCameraId;
    private int nextCameraId;
    public Camera mainNFOVCamera;
    private Camera cameraCalculate;
    private PanelVideoController panelVideoController;
    private OpticalFlowCamerasController opticalFlowCamerasController;
    private VideoPlayer videoPlayer;
    private bool reachable;
    private int oldFrame;
    private int nextKeyFrame;

    public Image[] imageArrowList;

    private int selectedDirection;

    private List<float[,]> downsamplePixelBlend;
    private List<Vector2[,]> downsamplePixelOpticalFlow;
    public List<Vector2>[] smoothPath;
    
    private float arrowShowTime;

    private List<bool>[] ifCloseCameraWindow;

    public float closeCameraWindowIouThreshold;
    public float allowedMaxIdleTime;
    public float minimalMeaningfulPixelSaliency;
    public float minimalMeaningfulRegionalSaliency;
    public float maxTransferKeyFrameNum;

    public float moveMaxPixelSpeed;
    public float moveMaxPixelPerFrame;

    public bool isReady;

    private Vector2 transferSpeed;

    private int mostValuableCamId = 0;

    private void Awake()
    {        
        manager = GameObject.Find("Manager").GetComponent<Manager>();
        mainNFOV23Content = manager.mainNFOV23Content;
        cameraCalculate = manager.cameraCalculate;
        panelVideoController = manager.panelVideoController;
        opticalFlowCamerasController = manager.opticalFlowCamerasController;
        videoPlayer = manager.videoPlayer;
        
        nowCameraId = -1;
        nextCameraId = -1;
        reachable = true;
        oldFrame = 0;
        nextKeyFrame = -1;
        selectedDirection = -1;
        

        arrowShowTime = 0;

        closeCameraWindowIouThreshold = 0.5f;
        allowedMaxIdleTime = 3;
        minimalMeaningfulPixelSaliency = 0.001f;
        minimalMeaningfulRegionalSaliency = 0.001f;
        maxTransferKeyFrameNum = 50;
        moveMaxPixelSpeed = 30;
        moveMaxPixelPerFrame = moveMaxPixelSpeed / videoPlayer.frameRate;
        Debug.Log("SceneNumber: " + Manager.GetActivateSceneNumber());
        transferSpeed = Vector2.zero;
    }

    // Use this for initialization
    void Start()
    {        
    }

    public void Init() {
        Clear();
        PrepareIfCloseCameraWindow();
        isReady = true;
    }
    public void Clear()
    {
        isReady = false;
    }
    public void PrepareIfCloseCameraWindow()
    {
        var path = opticalFlowCamerasController.smoothPath;
        if (path == null)
        {
            Debug.LogError("No smooth path");
            return;
        }
        InitDownsamplePixelBlendSaliencyMaskAndOpticalFlow();
        var camNum = panelVideoController.cameraGroupNum;
        var rectOfPixel = opticalFlowCamerasController.rectOfPixel;
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var regionalSaliency = opticalFlowCamerasController.regionalSaliency;
        ifCloseCameraWindow = new List<bool>[camNum];
        var totalKeyFrame = path[0].Count;
        for (int i = 0; i < camNum; i++) {
            ifCloseCameraWindow[i] = new List<bool>();
            for (int frame = 0; frame < totalKeyFrame; frame++)
                ifCloseCameraWindow[i].Add(false);
        }            

        var frameBeforeInitialIdleKeyFrame = new int[camNum];
        for (int camId = 0; camId < camNum; camId++)
            frameBeforeInitialIdleKeyFrame[camId] = -1;

        var camValueList = new float[camNum];
        for (int frame = 0; frame < totalKeyFrame; frame++)
        {
            for (int i = 0; i < camNum; i++)
            {
                var posTmp = path[i][frame];
                camValueList[i] += downsamplePixelBlend[frame][(int)posTmp.x, (int)posTmp.y];
                if (ifCloseCameraWindow[i][frame])
                    continue;
                for (int j = i + 1; j < camNum; j++)
                {
                    if (ifCloseCameraWindow[j][frame])
                        continue;
                    int overlapBeginFrame = frame;
                    int overlapEndFrame = frame - 1;
                    float sumI = 0;
                    float sumJ = 0;
                    for (int k = frame; k < totalKeyFrame; k++) {
                        var posI = path[i][k];
                        var rectI = rectOfPixel[(int)posI.x, (int)posI.y];
                        var singleAreaI = OpticalFlowCamerasController.GetRectWidth(rectI) * OpticalFlowCamerasController.GetRectHeight(rectI);
                        var posJ = path[j][k];
                        var rectJ = rectOfPixel[(int)posJ.x, (int)posJ.y];
                        var singleAreaJ = OpticalFlowCamerasController.GetRectWidth(rectJ) * OpticalFlowCamerasController.GetRectHeight(rectJ);
                        var overlapArea = opticalFlowCamerasController.GetRectRectOverlapArea(rectI, rectJ);
                        var iou = overlapArea / (singleAreaI + singleAreaJ - overlapArea);
                        if (iou >= closeCameraWindowIouThreshold)
                        {
                            sumI += downsamplePixelBlend[k][(int)posI.x, (int)posI.y];
                            sumJ += downsamplePixelBlend[k][(int)posJ.x, (int)posJ.y];
                            overlapEndFrame = k;
                        }
                        else {
                            break;
                        }
                    }
                    int closeCamId = sumI < sumJ ? i : j;
                    for (int k = overlapBeginFrame; k <= overlapEndFrame; k++) {
                        ifCloseCameraWindow[closeCamId][k] = true;
                    }
                }
            }
        }

        float tmpMax = -1;
        for (int i = 0; i < camValueList.Length; i++)
            if (camValueList[i] > tmpMax) {
                tmpMax = camValueList[i];
                mostValuableCamId = i;
            }

        for (int frame = 0; frame < totalKeyFrame; frame++)
        {
            for (int i = 0; i < camNum; i++)
            {
                if (ifCloseCameraWindow[i][frame])
                    continue;
                var posI = path[i][frame];
                bool ifUpdateCloseCameraWindow = false;
                int frameAfterLastIdelKeyFrame = frame;
                //Debug.Log("posI: " + posI);
                if (downsamplePixelBlend[frame][(int)posI.x, (int)posI.y] >= minimalMeaningfulPixelSaliency || regionalSaliency[frame][(int)posI.x, (int)posI.y] >= minimalMeaningfulRegionalSaliency)
                {
                    ifUpdateCloseCameraWindow = true;
                }
                else if (frame == totalKeyFrame - 1)
                {
                    ifUpdateCloseCameraWindow = true;
                    frameAfterLastIdelKeyFrame = totalKeyFrame;
                }
                if (ifUpdateCloseCameraWindow)
                {
                    float idleTime = ((float)frameAfterLastIdelKeyFrame - frameBeforeInitialIdleKeyFrame[i] - 1) / totalKeyFrame * videoPlayer.frameCount / videoPlayer.frameRate;
                    if (idleTime > allowedMaxIdleTime)
                    {
                        for (int j = frameBeforeInitialIdleKeyFrame[i] + 1; j < frameAfterLastIdelKeyFrame; j++)
                            ifCloseCameraWindow[i][j] = true;
                    }
                    frameBeforeInitialIdleKeyFrame[i] = frameAfterLastIdelKeyFrame;
                }
            }
        }

        for (int frame = 0; frame < totalKeyFrame; frame++)
        {
            int closedCamCnt = 0;
            int chosenCamId = 0;
            float maxV = 0;
            for (int camId = 0; camId < camNum; camId++)
            {
                if (ifCloseCameraWindow[camId][frame])
                {
                    closedCamCnt++;
                }
                var pos = path[camId][frame];
                var nowV = Mathf.Max(downsamplePixelBlend[frame][(int)pos.x, (int)pos.y], regionalSaliency[frame][(int)pos.x, (int)pos.y]);
                if (maxV < nowV)
                {
                    maxV = nowV;
                    chosenCamId = camId;
                }
            }
            if (closedCamCnt == camNum)
            {
                //Debug.Log(string.Format("ifCloseCameraWindow.count: {0}, frame: {1}, totalKeyFrame: {2}", ifCloseCameraWindow.Length, frame, totalKeyFrame));
                //Debug.Log("ifCloseCameraWindow[chosenCamId].Count: " + ifCloseCameraWindow[chosenCamId].Count);
                ifCloseCameraWindow[chosenCamId][frame] = false;
            }
        }
        for (int camId = 0; camId < camNum; camId++)
        {
            //Debug.Log("Camera" + camId + ":");
            for (int frame = 0; frame < totalKeyFrame; frame++)
            {
                var pos = path[camId][frame];
                //Debug.Log(string.Format("blendPixelSaliency: {0}, regionalSaliency: {1}", downsamplePixelBlendSaliencyMask[frame][(int)pos.x, (int)pos.y], regionalSaliency[frame][(int)pos.x, (int)pos.y]));
            }
            //Debug.Log(".................");
        }
        Debug.Log("ifCloseCameraWindow[0].Count: " + ifCloseCameraWindow[0].Count);
        Debug.Log("......................");
    }

    private void InitDownsamplePixelBlendSaliencyMaskAndOpticalFlow()
    {
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        //downsamplePixelOpticalFlow = opticalFlowCamerasController.GetDownsamplePixelOpticalFlow(downsampleWidth, downsampleHeight, true);
        //downsamplePixelBlendSaliencyMask = opticalFlowCamerasController.GetDownsamplePixelBlendSaliencyMask(downsampleWidth, downsampleHeight, false);
        downsamplePixelOpticalFlow = opticalFlowCamerasController.downsamplePixelOpticalFlow;
        downsamplePixelBlend = opticalFlowCamerasController.downsamplePixelBlend;
        smoothPath = opticalFlowCamerasController.smoothPath;
    }


    private int[] GetEveryDirectionCamId()
    {
        int uId = nowCameraId;
        int dId = nowCameraId;
        int lId = nowCameraId;
        int rId = nowCameraId;

        float uv = 0;
        float dv = 0;
        float lv = 0;
        float rv = 0;

        int width = videoPlayer.targetTexture.width;
        int height = videoPlayer.targetTexture.height;

        var mainCamPixel = panelVideoController.EulerAngleToPixel(mainNFOVCamera.transform.eulerAngles);
        for (int camId = 0; camId < panelVideoController.cameraGroupNum; camId++)
        {
            if (camId == nowCameraId)
                continue;
            var pixel = panelVideoController.EulerAngleToPixel(panelVideoController.cameraNFOVs[camId].transform.eulerAngles);
            if (pixel.y < mainCamPixel.y)
            {//up
                var upOffset = mainCamPixel.y - pixel.y;
                if (uId == nowCameraId || upOffset < uv)
                {
                    uId = camId;
                    uv = upOffset;
                }
            }
            if (pixel.y > mainCamPixel.y)
            {//down
                var downOffset = pixel.y - mainCamPixel.y;
                if (dId == nowCameraId || downOffset < dv)
                {
                    dId = camId;
                    dv = downOffset;
                }
            }
            var tmp = mainCamPixel.x - pixel.x;
            var leftOffset = tmp < 0 ? tmp + width : tmp;
            var rightOffset = tmp > 0 ? width - tmp : -tmp;
            if (lId == nowCameraId || leftOffset < lv)
            {//left
                lId = camId;
                lv = leftOffset;
            }
            if (rId == nowCameraId || rightOffset < rv)//right
            {
                rId = camId;
                rv = rightOffset;
            }
        }
        return new int[] { uId, dId, lId, rId };
    }
    public Vector2 GetVector2Of2Pixels(Vector2 p1, Vector2 p2, float w, int direction)
    {
        float vx = p2.x - p1.x;
        if (vx > w / 2)
            vx -= w;
        else if (vx < -w / 2)
            vx += w;
        if (direction == 2 && vx > 0)
        {
            vx -= w;
        }
        else if (direction == 3 && vx < 0)
        {
            vx += w;
        }
        return new Vector2(vx, p2.y - p1.y);
    }
    public int GetCurrentKeyFrame()
    {
        if (smoothPath == null|| smoothPath.Length==0)
        {
            return 0;
        }
        int totalKeyFrame = smoothPath[0].Count; ;
        return (int)(Mathf.Min(videoPlayer.frame, videoPlayer.frameCount - 1) * totalKeyFrame / videoPlayer.frameCount);
    }

    public void ChangeSelectedCamIdByCamId(int camId) {
        Debug.Log("ChangeSelectedCamIdByCamId: "+camId);
        if (camId == nextCameraId)
            return;
        var nowFrame = (int)videoPlayer.frame;
        var totalFrame = videoPlayer.frameCount;
        int totalKeyFrame = smoothPath[0].Count;
        var beginKeyFrame = GetCurrentKeyFrame();
        if (ifCloseCameraWindow[camId][beginKeyFrame])
            return;
        var originSize = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var downsampleSize = new Vector2(downsampleWidth, downsampleHeight);
        var nowDownsamplePixel = opticalFlowCamerasController.PixelToOriginPixelFloatValue(panelVideoController.EulerAngleToPixel(mainNFOVCamera.transform.eulerAngles), originSize, downsampleSize);
        bool ifGetNextCam = false;
        var minV = new Vector2(1e9f, 1e9f);
        int newNowFrame = (int)videoPlayer.frame;
        Debug.Log("newNowFrame: " + newNowFrame);
        int nextCameraIdTmp = 0;

        int nextJustOkCamIdTmp = 0;
        float justOkV=1e9f;
        int justOkKeyFrame = Mathf.Min(totalKeyFrame - 1, beginKeyFrame + 1);
        for (int i = beginKeyFrame + 1; i < Mathf.Min(totalKeyFrame, beginKeyFrame + maxTransferKeyFrameNum); i++)
        {
            int frameNum = KeyFrameToFrame(i) - nowFrame;
            if (frameNum < 0)
                continue;
            float timeGap = frameNum / videoPlayer.frameRate;
            if (nextCameraId == nowCameraId && nowCameraId == camId)
                continue;
            var path = opticalFlowCamerasController.smoothPath[camId];
            var futureDownsamplePixel = path[i];
            var v = GetVector2Of2Pixels(nowDownsamplePixel, futureDownsamplePixel, downsampleWidth, -1) / timeGap;
            if (v.magnitude <= moveMaxPixelSpeed)
            {
                if (v.magnitude < minV.magnitude)
                {
                    nextCameraIdTmp = camId;
                    ifGetNextCam = true;
                    minV = v;
                }
            }
            else {
                if (justOkV > v.magnitude) {
                    justOkV = v.magnitude;
                    nextJustOkCamIdTmp = camId;
                    justOkKeyFrame = i;
                }
            }     
            if (ifGetNextCam)
            {
                ChangeHintFrameStatus(nextCameraId, false);
                ChangeHintFrameStatus(nextCameraIdTmp, true);
                nextCameraId = nextCameraIdTmp;
                nextKeyFrame = i;
                reachable = true;
                selectedDirection = -1;
                oldFrame = nowFrame;
                var nextFrame = KeyFrameToFrame(nextKeyFrame);
                if (nextFrame < nowFrame)
                {
                    Debug.Log(string.Format("Strange!oldFrame:{0}, nowFrame:{1}, nextFrame:{2}", oldFrame, nowFrame, nextFrame));
                }
                transferSpeed = minV;
                //ShowImageArrow();
                break;
            }
        }
        if (!ifGetNextCam) {
            ChangeHintFrameStatus(nextCameraId, false);
            ChangeHintFrameStatus(nextJustOkCamIdTmp, true);
            nextCameraId = nextJustOkCamIdTmp;
            nextKeyFrame = justOkKeyFrame;
            reachable = false;
            selectedDirection = -1;
            oldFrame = nowFrame;
            transferSpeed = minV;
        }
    }
    public void ClearHintFrameStatus() {
        var selectedHintFrames = panelVideoController.selectedHintFrames;
        for (int camId = 0; camId < selectedHintFrames.Length; camId++) {
            ChangeHintFrameStatus(camId, false);
        }
    }
    private void ChangeHintFrameStatus(int camId,bool ifShow) {
        var selectedHintFrames = panelVideoController.selectedHintFrames;
        if (0 <= camId && camId < selectedHintFrames.Length) {
            selectedHintFrames[camId].gameObject.SetActive(ifShow);
        }
    }
    private void ChangeSelectedCamIdByDirection(int direction)
    {
        var pvc = manager.panoramaVideoController;
        
        Debug.Log(string.Format("ChangeSelectedCamIdByDirection, nowCameraId:{0}", nowCameraId));
        int totalKeyFrame = smoothPath[0].Count;
        var nowFrame = (int)videoPlayer.frame;
        var totalFrame = videoPlayer.frameCount;
        int beginKeyFrame = Mathf.Min((int)((float)nowFrame * totalKeyFrame / totalFrame), totalKeyFrame - 1) + 1;
        var originSize = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var downsampleSize = new Vector2(downsampleWidth, downsampleHeight);
        var nowDownsamplePixel = opticalFlowCamerasController.PixelToOriginPixelFloatValue(panelVideoController.EulerAngleToPixel(mainNFOVCamera.transform.eulerAngles), originSize, downsampleSize);
        bool ifGetNextCam = false;
        bool ifReachable = false;
        int nextKeyFrameTmp = Mathf.Min(totalKeyFrame - 1, beginKeyFrame);
        var minV = new Vector2(1e9f, 1e9f);
        int newNowFrame = (int)videoPlayer.frame;
        int nextCameraIdTmp = 0;
        for (int i = beginKeyFrame; i < Mathf.Min(totalKeyFrame, beginKeyFrame + maxTransferKeyFrameNum); i++)
        {
            int frameNum = KeyFrameToFrame(i) - nowFrame;
            if (frameNum < 0)
                continue;
            float timeGap = frameNum / videoPlayer.frameRate;
            for (int camId = 0; camId < panelVideoController.cameraGroupNum; camId++)
            {
                if (nextCameraId == nowCameraId && nowCameraId == camId)
                    continue;
                if (ifCloseCameraWindow != null && ifCloseCameraWindow[camId][i])
                    continue;
                var path = smoothPath[camId];
                var futureDownsamplePixel = path[i];
                var v = GetVector2Of2Pixels(nowDownsamplePixel, futureDownsamplePixel, downsampleWidth, direction) / timeGap;
                bool ifOk = false;
                switch (direction)
                {
                    case 0:
                        ifOk = v.y < 0;
                        break;
                    case 1:
                        ifOk = v.y > 0;
                        break;
                    case 2:
                        ifOk = v.x < 0;
                        break;
                    case 3:
                        ifOk = v.x > 0;
                        break;
                }
                if (ifOk)
                {
                    if (v.magnitude < minV.magnitude)
                    {
                        nextCameraIdTmp = camId;
                        ifGetNextCam = true;
                        minV = v;
                        nextKeyFrameTmp = i;
                        if (v.magnitude <= moveMaxPixelSpeed)
                        {
                            ifReachable = true;
                        }
                    }
                }
            }
            if (ifReachable)
                break;
        }
        if (ifGetNextCam)
        {
            Debug.Log("GetNextCam");
            ChangeHintFrameStatus(nextCameraId, false);
            ChangeHintFrameStatus(nextCameraIdTmp, true);
            nextCameraId = nextCameraIdTmp;
            nextKeyFrame = nextKeyFrameTmp;
            reachable = ifReachable;
            selectedDirection = direction;
            oldFrame = nowFrame;
            transferSpeed = minV;
            var nextFrame = KeyFrameToFrame(nextKeyFrame);
            if (nextFrame < nowFrame)
            {
                Debug.Log(string.Format("Strange!oldFrame:{0}, nowFrame:{1}, nextFrame:{2}", oldFrame, nowFrame, nextFrame));
            }
            ShowImageArrow();
            if (manager.panoramaVideoController.IfUseKoreaPlusMultiMethod())
            {
                manager.panoramaVideoController.koreaMethod.InitKoreaMethodPaths();
            }            
        }
        else
        {
            Debug.Log("no next cam");
        }
    }

    private void RespondDirectionKey()
    {
        if (Input.anyKeyDown)
        {
            Debug.Log("KeyDown!");
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            {
                ChangeSelectedCamIdByDirection(0);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            {
                ChangeSelectedCamIdByDirection(1);
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
            {
                ChangeSelectedCamIdByDirection(2);
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            {
                ChangeSelectedCamIdByDirection(3);
            }
        }
    }
    private void ChangeToNearestCamId()
    {
        var pvc = manager.panoramaVideoController;
        Debug.Log(string.Format("ChangeSelectedCamIdByDirection, nowCameraId:{0}", nowCameraId));
        int totalKeyFrame = smoothPath[0].Count;
        var nowFrame = (int)videoPlayer.frame;
        var totalFrame = videoPlayer.frameCount;
        int beginKeyFrame = Mathf.Min((int)((float)nowFrame * totalKeyFrame / totalFrame), totalKeyFrame - 1) + 1;
        var originSize = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var downsampleSize = new Vector2(downsampleWidth, downsampleHeight);
        var nowDownsamplePixel = opticalFlowCamerasController.PixelToOriginPixelFloatValue(panelVideoController.EulerAngleToPixel(mainNFOVCamera.transform.eulerAngles), originSize, downsampleSize);
        bool ifGetNextCam = false;
        bool ifReachable = false;
        int nextKeyFrameTmp = Mathf.Min(totalKeyFrame - 1, beginKeyFrame);
        var minV = new Vector2(1e9f, 1e9f);
        int newNowFrame = (int)videoPlayer.frame;
        int nextCameraIdTmp = 0;
        for (int i = beginKeyFrame; i < Mathf.Min(totalKeyFrame, beginKeyFrame + maxTransferKeyFrameNum); i++)
        {
            int frameNum = KeyFrameToFrame(i) - nowFrame;
            if (frameNum < 0)
                continue;
            float timeGap = frameNum / videoPlayer.frameRate;
            for (int camId = 0; camId < panelVideoController.cameraGroupNum; camId++)
            {
                if (nextCameraId == nowCameraId && nowCameraId == camId)
                    continue;
                if (ifCloseCameraWindow != null && ifCloseCameraWindow[camId][i])
                    continue;
                var path = smoothPath[camId];
                var futureDownsamplePixel = path[i];
                var v = GetVector2Of2Pixels(nowDownsamplePixel, futureDownsamplePixel, downsampleWidth, 0) / timeGap;
                bool ifOk = true;
                if (ifOk)
                {
                    if (v.magnitude < minV.magnitude)
                    {
                        nextCameraIdTmp = camId;
                        ifGetNextCam = true;
                        minV = v;
                        nextKeyFrameTmp = i;
                        if (v.magnitude <= moveMaxPixelSpeed)
                        {
                            ifReachable = true;
                        }
                    }
                }
            }
            if (ifReachable)
                break;
        }
        if (ifGetNextCam)
        {
            Debug.Log("GetNextCam");
            ChangeHintFrameStatus(nextCameraId, false);
            ChangeHintFrameStatus(nextCameraIdTmp, true);
            nextCameraId = nextCameraIdTmp;
            nextKeyFrame = nextKeyFrameTmp;
            reachable = ifReachable;
            selectedDirection = 0;
            oldFrame = nowFrame;
            transferSpeed = minV;
            var nextFrame = KeyFrameToFrame(nextKeyFrame);
            if (nextFrame < nowFrame)
            {
                Debug.Log(string.Format("Strange!oldFrame:{0}, nowFrame:{1}, nextFrame:{2}", oldFrame, nowFrame, nextFrame));
            }
            if (manager.panoramaVideoController.IfUseKoreaPlusMultiMethod())
            {
                manager.panoramaVideoController.koreaMethod.InitKoreaMethodPaths();
            }
        }
        else
        {
            Debug.Log("no next cam");
        }
    }
    // Update is called once per frame
    void Update()
    {
        if (isReady) {
            var camGroupNum = panelVideoController.cameraGroupNum;
            UpdateMainNFOVCamera();
            arrowShowTime = Mathf.Max(arrowShowTime - Time.deltaTime, 0);
            SetTransparencyOfImageArrow(arrowShowTime / initArrowShowTime);
            RespondDirectionKey();
            UpdatePanelNFOV_ShowStatus();
            var keyFrame = GetCurrentKeyFrame();
            if (nowCameraId != -1 && nowCameraId == nextCameraId && ifCloseCameraWindow[nowCameraId][keyFrame]) {
                ChangeToNearestCamId();
            }
            //Debug.Log(string.Format("nowCamId:{0}, nextCamId:{1}, nowKeyFrame:{2}, nextKeyFrame:{3}, ifFollowMode:{4}, reachable:{5}", nowCameraId, nextCameraId, GetCurrentKeyFrame(), nextKeyFrame, IfFollowMode(), reachable));
        }
    }
    private void UpdatePanelNFOV_ShowStatus()
    {
        if (ifCloseCameraWindow == null)
            return;
        var panelNFOV = panelVideoController.panelNFOV;
        if (panelNFOV == null)
            return;
        if (manager.panoramaVideoController.IfUseKoreaPlusMultiPlusPipMethod())
            panelNFOV = manager.panoramaVideoController.pipMethod.pipList;
        int totalKeyframe = smoothPath[0].Count;
        int frame = GetCurrentKeyFrame();
        int camNum = panelVideoController.cameraGroupNum;
        for (int camId = 0; camId < camNum; camId++)
            UpdatePanelNFOVVeilStatus(panelNFOV[camId], ifCloseCameraWindow[camId][frame]);
    }

    public bool IfCameraWindowClosed(int camId) {
        if (ifCloseCameraWindow == null)
            return false;
        int frame = GetCurrentKeyFrame();
        return ifCloseCameraWindow[camId][frame];
    }

    private void UpdatePanelNFOVVeilStatus(GameObject panelNFOV, bool ifclose) {        
        if (manager.panoramaVideoController.IfUseKoreaPlusMultiPlusPipMethod())
        {
            panelNFOV.SetActive(ifclose);
        }
        else {
            var image = panelNFOV.transform.Find("Veil").GetComponent<Image>();
            var c = image.color;
            c.a = ifclose ? 180f / 255 : 0;
            image.color = c;
        }
    }
    private void ShowImageArrow()
    {
        arrowShowTime = initArrowShowTime;
        foreach (var imageArrow in imageArrowList)
        {
            var rt = imageArrow.GetComponent<RectTransform>();
            var ea = rt.eulerAngles;
            switch (selectedDirection)
            {
                case 0:
                    rt.eulerAngles = new Vector3(ea.x, ea.y, 0);
                    break;
                case 1:
                    rt.eulerAngles = new Vector3(ea.x, ea.y, 180);
                    break;
                case 2:
                    rt.eulerAngles = new Vector3(ea.x, ea.y, 90);
                    break;
                case 3:
                    rt.eulerAngles = new Vector3(ea.x, ea.y, 270);
                    break;
            }
            var c = imageArrow.color;
            c.a = 1;
            imageArrow.color = c;
        }
    }
    private void SetTransparencyOfImageArrow(float a)
    {
        foreach (var imageArrow in imageArrowList)
        {
            var c = imageArrow.color;
            c.a = a;
            imageArrow.color = c;
            //Debug.Log("arrow color:"+c);
        }
    }
    public int KeyFrameToFrame(int keyFrame)
    {
        int totalKeyFrame = smoothPath[0].Count;
        return (int)((float)keyFrame / totalKeyFrame * videoPlayer.frameCount);
    }

    public void UpdateMainNFOVCamera()
    {
        if (opticalFlowCamerasController.smoothPath == null || !videoPlayer.isPlaying)
            return;
        var originSize = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var downsampleSize = new Vector2(downsampleWidth, downsampleHeight);
        int nowFrame = (int)videoPlayer.frame;
        var cameraNFOVs = panelVideoController.cameraNFOVs;        
        var nextFrame = KeyFrameToFrame(nextKeyFrame);
        //Debug.Log(string.Format("oldFrame: {0}, nowFrame: {1}, nextFrame: {2}", oldFrame, nowFrame, nextFrame));
        if (nextCameraId >= 0)
        {            
            if (nowFrame < nextFrame || !reachable) {
                var path = opticalFlowCamerasController.smoothPath[nextCameraId];
                var mForward = mainNFOVCamera.transform.forward;
                var desP = path[nextKeyFrame];
                var nowP = opticalFlowCamerasController.PixelToOriginPixelFloatValue(panelVideoController.EulerAngleToPixel(mainNFOVCamera.transform.eulerAngles), originSize, downsampleSize);
                //Debug.Log(string.Format("frames: {0}, nowP: {1}, Time.deltaTime:{2}", (nowFrame - oldFrame), nowP, Time.deltaTime));
                var v = GetVector2Of2Pixels(nowP, desP, downsampleWidth, selectedDirection);
                var pixelDistPerFrame = nextFrame - oldFrame <= 0 ? 1e9f : v.magnitude / (nextFrame - oldFrame);
                pixelDistPerFrame = Mathf.Min(moveMaxPixelPerFrame, pixelDistPerFrame);
                var theta = Mathf.Max((nowFrame - oldFrame) * pixelDistPerFrame, 0);
                var nv = v.normalized;
                //var nextP = nowP + new Vector2(nv.x * theta, nv.y * theta);
                var nextP = nowP + Mathf.Min(transferSpeed.magnitude, moveMaxPixelSpeed) * Time.deltaTime * transferSpeed.normalized;//只用帧数的话会比较卡顿                
                OpticalFlowCamerasController.NormalizePixelInRange(ref nextP.x, ref nextP.y, downsampleWidth, downsampleHeight);
                var newForward = panelVideoController.PixelToVector3(opticalFlowCamerasController.PixelToOriginPixelFloatValue(nextP, downsampleSize, originSize));
                mainNFOVCamera.transform.forward = newForward;
                manager.panoramaVideoController.UpdateTextMethodStatus(2);
                mainNFOVCamera.Render();                
            }
            else
            {
                mainNFOVCamera.transform.forward = cameraNFOVs[nextCameraId].transform.forward;
                manager.panoramaVideoController.UpdateTextMethodStatus(2);
                mainNFOVCamera.Render();
                nextKeyFrame = -1;
                nowCameraId = nextCameraId;
                selectedDirection = -1;
            }
        }
        oldFrame = nowFrame;        
    }

    public void VideoNowFrameChangedByUser()
    {
        oldFrame = (int)videoPlayer.frame;
        nextKeyFrame = -1;
        reachable = true;
        nowCameraId = -1;
        if (nextCameraId != -1)
            ChangeHintFrameStatus(nextCameraId, false);
        nextCameraId = -1;
        //ifTrackMode = false;
    }
    public bool IfFollowMode() {
        return isReady && nextCameraId != -1;
    }
    public void ReplayInit() {
        Debug.Log("***ReplayInit,frame: " + videoPlayer.frame);
        opticalFlowCamerasController.UserStudySceneUpdateCameras();
        ClearHintFrameStatus();
        VideoNowFrameChangedByUser();
        var cameraNFOVs = panelVideoController.cameraNFOVs;
        mostValuableCamId = Mathf.Min(cameraNFOVs.Length - 1, 1);
        mainNFOVCamera.transform.forward = cameraNFOVs[mostValuableCamId].transform.forward;                
        mainNFOVCamera.Render();
        ChangeSelectedCamIdByCamId(mostValuableCamId);
    }

    public void SwitchFollowMode() {
        if (IfFollowMode())
        {
            manager.panoramaVideoController.ChangedManually();
        }
        else {
            ChangeToNearestCamId();
        }
    }
}
