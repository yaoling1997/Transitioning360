using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class MainNFOVController : MonoBehaviour {        
    private static readonly float moveMaxPixelSpeed = 60;//How large is the move vector size on the rectangle per second when switching windows   
    private static readonly float initArrowShowTime = 1;
    private Manager manager;
    private RawImage mainNFOVContent;//NFOV viewed by users  
    private int nowCameraId;//The ID of the camera selected for the current primary view
    private int nextCameraId;//The ID of the next selected camera
    private Camera mainNFOVCamera;
    private Camera cameraCalculate;//Camera for computing, not for imaging
    private PanelVideoController panelVideoController;
    private OpticalFlowCamerasController opticalFlowCamerasController;
    private VideoPlayer videoPlayer;
    private bool reachable;//Is the keyframe reachable in video time    
    private int oldFrame;//Real frame when mainfovcamera is not updated
    private int nextKeyFrame;//The next keyframe that coincides with selectedcameraid      

    private Texture2D locatorTexture;//Presents the texture of locator
    private Vector2 locatorPos;    //Position of positioner
    private List<Vector2> locatorShape;
    public float locatorTextureMaxLength;
    public RawImage locator;//The locator shows the process of the center movement of the main NFOV
    public Image[] imageArrowList;

    private int selectedDirection;//Select the up and down direction of - 1,2,0

    private List<float[,]> downsamplePixelBlendSaliencyMask;
    private List<Vector2[,]> downsamplePixelOpticalFlow;

    //Displays the time remaining for the arrow
    private float arrowShowTime;

    //Do you want to close the window of frame J of I camera to prohibit transfer
    private List<bool>[] ifCloseCameraWindow;

    //If the IOU of two windows is greater than, one of them will be closed
    public float closeCameraWindowIouThreshold;
    //Maximum time allowed to shoot meaningless content, unit: s
    public float allowedMaxIdleTime;
    //The smallest meaningful pixel salience
    public float minimalMeaningfulPixelSaliency;
    //The smallest meaningful salience
    public float minimalMeaningfulRegionalSaliency;
    //The most transfer process takes keyframes
    public float maxTransferKeyFrameNum;


    private void Awake()
    {
        Debug.Log("MainNFOVController.Awake");
        manager = GameObject.Find("Manager").GetComponent<Manager>();
        mainNFOVContent = manager.mainNFOVContent;
        cameraCalculate = manager.cameraCalculate;
        panelVideoController = manager.panelVideoController;
        opticalFlowCamerasController = manager.opticalFlowCamerasController;
        videoPlayer = manager.videoPlayer;
        
        nowCameraId = 0;
        nextCameraId = 0;        
        mainNFOVCamera = Instantiate(cameraCalculate);
        mainNFOVCamera.name = "MainNFOVCamera";
        var t = Instantiate(cameraCalculate.targetTexture);
        t.name = "MainNFOVCameraTexture";
        mainNFOVCamera.targetTexture = t;
        var ri = mainNFOVContent.GetComponent<RawImage>();
        ri.texture = t;        
        reachable = true;
        oldFrame = 0;
        nextKeyFrame = -1;
        locatorPos = Vector2.zero;
        locatorShape = new List<Vector2>();
        for (int i = -5; i <= 5; i++)
            for (int j = -1; j <= 1; j++) {
                locatorShape.Add(new Vector2(i, j));
                locatorShape.Add(new Vector2(j, i));
            }
        selectedDirection = -1;

        arrowShowTime = 0;

        closeCameraWindowIouThreshold = 0.3f;
        allowedMaxIdleTime = 3;
        minimalMeaningfulPixelSaliency = 0.02f;
        minimalMeaningfulRegionalSaliency = 0.02f;
        maxTransferKeyFrameNum = 25;
        Debug.Log("SceneNumber: "+Manager.GetActivateSceneNumber());
    }

    // Use this for initialization
    void Start () {
        panelVideoController.selectedHintFrames[nowCameraId].gameObject.SetActive(true);
    }

    //Which camera windows of which frames are ready to close
    public void PrepareIfCloseCameraWindow() {
        var path = opticalFlowCamerasController.smoothPath;
        if (path == null) {
            Debug.Log("No smooth path");
            return;
        }
        InitDownsamplePixelBlendSaliencyMaskAndOpticalFlow();
        var camNum = panelVideoController.cameraGroupNum;
        var rectOfPixel = opticalFlowCamerasController.rectOfPixel;
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth,out downsampleHeight);        
        var regionalSaliency = opticalFlowCamerasController.regionalSaliency;
        ifCloseCameraWindow = new List<bool>[camNum];
        var totalKeyFrame = path[0].Count;
        for (int i = 0; i < camNum; i++)
            ifCloseCameraWindow[i] = new List<bool>();

        //Which is the previous frame of the idle frame
        var frameBeforeInitialIdleKeyFrame = new int[camNum];
        for (int camId = 0; camId < camNum; camId++)
            frameBeforeInitialIdleKeyFrame[camId] = -1;
        for (int frame=0;frame<totalKeyFrame;frame++) {
            for (int i = 0; i < camNum; i++) {
                ifCloseCameraWindow[i].Add(false);
            }
            for (int i = 0; i < camNum; i++) {
                var posI = path[i][frame];
                bool ifUpdateCloseCameraWindow = false;
                int frameAfterLastIdelKeyFrame = frame;
                if (downsamplePixelBlendSaliencyMask[frame][(int)posI.x, (int)posI.y] >= minimalMeaningfulPixelSaliency|| regionalSaliency[frame][(int)posI.x, (int)posI.y]>= minimalMeaningfulRegionalSaliency)
                {
                    ifUpdateCloseCameraWindow = true;
                }
                else if (frame == totalKeyFrame - 1) {
                    ifUpdateCloseCameraWindow = true;
                    frameAfterLastIdelKeyFrame = totalKeyFrame;
                }
                if (ifUpdateCloseCameraWindow) {
                    float idleTime = ((float)frameAfterLastIdelKeyFrame - frameBeforeInitialIdleKeyFrame[i] - 1) / totalKeyFrame * videoPlayer.frameCount / videoPlayer.frameRate;
                    if (idleTime > allowedMaxIdleTime)
                    {
                        for (int j = frameBeforeInitialIdleKeyFrame[i] + 1; j < frameAfterLastIdelKeyFrame; j++)
                            ifCloseCameraWindow[i][j] = true;
                    }
                    frameBeforeInitialIdleKeyFrame[i] = frameAfterLastIdelKeyFrame;
                }
            }

            for (int i = 0; i < camNum; i++) {
                if (ifCloseCameraWindow[i][frame])
                    continue;
                var posI = path[i][frame];
                var rectI = rectOfPixel[(int)posI.x, (int)posI.y];
                var singleAreaI = OpticalFlowCamerasController.GetRectWidth(rectI) * OpticalFlowCamerasController.GetRectHeight(rectI);
                for (int j = i+1; j < camNum; j++) {
                    if (ifCloseCameraWindow[j][frame])
                        continue;
                    var posJ = path[j][frame];
                    var rectJ = rectOfPixel[(int)posJ.x, (int)posJ.y];
                    var singleAreaJ = OpticalFlowCamerasController.GetRectWidth(rectJ) * OpticalFlowCamerasController.GetRectHeight(rectJ);
                    var overlapArea = opticalFlowCamerasController.GetRectRectOverlapArea(rectI, rectJ);
                    var iou = overlapArea / (singleAreaI + singleAreaJ - overlapArea);
                    if (iou >= closeCameraWindowIouThreshold) {
                        if (downsamplePixelBlendSaliencyMask[frame][(int)posI.x, (int)posI.y] < downsamplePixelBlendSaliencyMask[frame][(int)posJ.x, (int)posJ.y])
                        {
                            ifCloseCameraWindow[i][frame] = true;
                        }
                        else {
                            ifCloseCameraWindow[j][frame] = true;
                        }
                    }
                }
            }
        }
        //Make sure that at least one camera is on for each frame
        for (int frame = 0; frame < totalKeyFrame; frame++) {
            int closedCamCnt = 0;
            int chosenCamId = 0;
            float maxV = 0;
            for (int camId = 0; camId < camNum; camId++) {
                if (ifCloseCameraWindow[camId][frame]) {
                    closedCamCnt++;
                }
                var pos = path[camId][frame];
                var nowV = Mathf.Max(downsamplePixelBlendSaliencyMask[frame][(int)pos.x, (int)pos.y],regionalSaliency[frame][(int)pos.x, (int)pos.y]);
                if (maxV < nowV) {
                    maxV = nowV;
                    chosenCamId = camId;
                }
            }
            if (closedCamCnt == camNum)//Restore the most meaningful camera
            {
                ifCloseCameraWindow[chosenCamId][frame] = false;
            }
        }
        for (int camId = 0; camId < camNum; camId++) {
            Debug.Log("Camera"+camId+":");
            for (int frame = 0; frame < totalKeyFrame; frame++) {
                var pos = path[camId][frame];                
                Debug.Log(string.Format("blendPixelSaliency: {0}, regionalSaliency: {1}", downsamplePixelBlendSaliencyMask[frame][(int)pos.x, (int)pos.y], regionalSaliency[frame][(int)pos.x, (int)pos.y]));
            }
            Debug.Log(".................");
        }
        Debug.Log("ifCloseCameraWindow[0].Count: "+ ifCloseCameraWindow[0].Count);
        Debug.Log("......................");
    }

    private void InitDownsamplePixelBlendSaliencyMaskAndOpticalFlow() {
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        downsamplePixelOpticalFlow = opticalFlowCamerasController.GetDownsamplePixelOpticalFlow(downsampleWidth, downsampleHeight, true);
        downsamplePixelBlendSaliencyMask = opticalFlowCamerasController.GetDownsamplePixelBlendSaliencyMask(downsampleWidth, downsampleHeight, false);
    }

    public void InitLocator() {
        var videoWidth = videoPlayer.targetTexture.width;
        var videoHeight = videoPlayer.targetTexture.height;
        var width = (int)Mathf.Min(locatorTextureMaxLength, videoWidth * locatorTextureMaxLength / videoHeight);
        var height = (int)Mathf.Min(locatorTextureMaxLength, videoHeight * locatorTextureMaxLength / videoWidth);
        if (locatorTexture != null)
            Destroy(locatorTexture);
        locatorTexture = new Texture2D(width, height);
        var c = locatorTexture.GetPixel(0, 0);
        Debug.Log(string.Format("init color r,g,b,a: {0}, {1}, {2}, {3}", c.r, c.g, c.b, c.a));
        for (int i = 0; i < width; i++)
            for (int j = 0; j < height; j++)
                locatorTexture.SetPixel(i, j, Color.clear);
        locatorTexture.Apply();
        locator.texture = locatorTexture;
    }

    //Get the ID of the next camera in each direction
    private int[] GetEveryDirectionCamId() {        
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
        for (int camId = 0; camId < panelVideoController.cameraGroupNum; camId++) {
            if (camId == nowCameraId)
                continue;
            var pixel = panelVideoController.EulerAngleToPixel(panelVideoController.cameraNFOVs[camId].transform.eulerAngles);
            if (pixel.y < mainCamPixel.y) {//up
                var upOffset = mainCamPixel.y - pixel.y;
                if (uId == nowCameraId || upOffset<uv) {
                    uId = camId;
                    uv = upOffset;
                }
            }
            if (pixel.y > mainCamPixel.y) {//down
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
            if (lId == nowCameraId || leftOffset < lv) {//left
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
        else if (direction == 3 && vx < 0) {
            vx += w;
        }
        return new Vector2(vx, p2.y - p1.y);
    }

    //The main view changes the selected camera ID
    private void ChangeSelectedCamId(int direction) {
        //It is consistent with the currently selected camera ID
        int totalKeyFrame = opticalFlowCamerasController.smoothPath[0].Count;
        var nowFrame = (int)videoPlayer.frame;
        var totalFrame = videoPlayer.frameCount;
        Debug.Log("nowFrame: "+ nowFrame);
        Debug.Log("totalKeyFrame: " + totalKeyFrame);
        Debug.Log("totalFrame: " + totalFrame);
        int beginKeyFrame = Mathf.Min((int)((float)nowFrame * totalKeyFrame / totalFrame), totalKeyFrame - 1) + 1;
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
        for (int i = beginKeyFrame; i < Mathf.Min(totalKeyFrame, beginKeyFrame + maxTransferKeyFrameNum); i++) {
            int frameNum = KeyFrameToFrame(i) - nowFrame;
            if (frameNum < 0)
                continue;
            float timeGap = frameNum / videoPlayer.frameRate;
            for (int camId=0;camId< panelVideoController.cameraGroupNum;camId++) {
                if (nextCameraId == nowCameraId && nowCameraId == camId)
                    continue;
                if (ifCloseCameraWindow != null && ifCloseCameraWindow[camId][i])
                    continue;
                var path = opticalFlowCamerasController.smoothPath[camId];
                var futureDownsamplePixel = path[i];
                var v = GetVector2Of2Pixels(nowDownsamplePixel, futureDownsamplePixel, downsampleWidth, direction);
                if (v.magnitude / timeGap <= moveMaxPixelSpeed)
                {
                    bool ifOk = false;
                    switch (direction) {
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
                    if (ifOk) {
                        if (v.magnitude < minV.magnitude) {
                            nextCameraIdTmp = camId;
                            ifGetNextCam = true;
                            minV = v;                            
                        }
                    }
                }
            }
            if (ifGetNextCam) {
                panelVideoController.selectedHintFrames[nextCameraId].gameObject.SetActive(false);
                panelVideoController.selectedHintFrames[nextCameraIdTmp].gameObject.SetActive(true);
                nextCameraId = nextCameraIdTmp;
                nextKeyFrame = i;
                reachable = true;
                selectedDirection = direction;
                oldFrame = nowFrame;
                var nextFrame = KeyFrameToFrame(nextKeyFrame);
                if (nextFrame < nowFrame)
                {
                    Debug.Log(string.Format("Strange!oldFrame:{0}, nowFrame:{1}, nextFrame:{2}", oldFrame, nowFrame, nextFrame));
                }
                ShowImageArrow();
                break;                
            }                
        }        
    }

    //Respond to user's direction key
    private void RespondDirectionKey() {
        if (Input.anyKeyDown) {
            if (Input.GetKeyDown(KeyCode.UpArrow)) {                
                ChangeSelectedCamId(0);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow)) {
                ChangeSelectedCamId(1);
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                ChangeSelectedCamId(2);
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                ChangeSelectedCamId(3);
            }
        }
    }

	// Update is called once per frame
	void Update () {        
        arrowShowTime = Mathf.Max(arrowShowTime - Time.deltaTime, 0);
        SetTransparencyOfImageArrow(arrowShowTime/initArrowShowTime);
        RespondDirectionKey();
        UpdatePanelNFOV_ShowStatus();
    }
    //Weather open and close the NFOV window
    private void UpdatePanelNFOV_ShowStatus(){
        if (ifCloseCameraWindow == null)
            return;
        var panelNFOV = panelVideoController.panelNFOV;
        int totalKeyframe = opticalFlowCamerasController.smoothPath[0].Count;
        int frame = Mathf.Min((int)((float)videoPlayer.frame * totalKeyframe / videoPlayer.frameCount), totalKeyframe - 1);
        int camNum = panelVideoController.cameraGroupNum;
        for (int camId = 0; camId < camNum; camId++)
            panelNFOV[camId].SetActive(!ifCloseCameraWindow[camId][frame]);        
    }
    //Arrow showing direction of movement
    private void ShowImageArrow() {
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
    //Set arrow transparency
    private void SetTransparencyOfImageArrow(float a) {
        foreach (var imageArrow in imageArrowList)
        {
            var c = imageArrow.color;
            c.a = a;
            imageArrow.color = c;
        }
    }
    
    public int GetCurrentKeyFrame() {
        int totalKeyFrame= opticalFlowCamerasController.smoothPath[0].Count;
        return (int)(Mathf.Min(videoPlayer.frame, videoPlayer.frameCount - 1) * totalKeyFrame / videoPlayer.frameCount);
    }
    //Converts keyframes to corresponding frames
    public int KeyFrameToFrame(int keyFrame)
    {
        int totalKeyFrame = opticalFlowCamerasController.smoothPath[0].Count;
        return (int)((float)keyFrame / totalKeyFrame * videoPlayer.frameCount);
    }

    public void UpdateMainNFOVCamera() {
        if (opticalFlowCamerasController.smoothPath == null || !videoPlayer.isPlaying)
            return;
        var originSize = new Vector2(videoPlayer.targetTexture.width, videoPlayer.targetTexture.height);
        int downsampleWidth, downsampleHeight;
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        var downsampleSize = new Vector2(downsampleWidth, downsampleHeight);
        int nowFrame = (int)videoPlayer.frame;
        var cameraNFOVs = panelVideoController.cameraNFOVs;
        var path = opticalFlowCamerasController.smoothPath[nextCameraId];
        var nextFrame = KeyFrameToFrame(nextKeyFrame);
        Debug.Log(string.Format("oldFrame: {0}, nowFrame: {1}, nextFrame: {2}",oldFrame ,nowFrame, nextFrame));        
        if (nowFrame <= nextFrame || !reachable)
        {
            var mForward = mainNFOVCamera.transform.forward;
            var desP = path[nextKeyFrame];
            var nowP = opticalFlowCamerasController.PixelToOriginPixelFloatValue(panelVideoController.EulerAngleToPixel(mainNFOVCamera.transform.eulerAngles), originSize, downsampleSize);
            var v = GetVector2Of2Pixels(nowP, desP, downsampleWidth, selectedDirection);
            var pixelDistPerFrame = nextFrame - oldFrame <= 0 ? 1e9f : v.magnitude / (nextFrame - oldFrame);
            pixelDistPerFrame = Mathf.Min(moveMaxPixelSpeed / videoPlayer.frameRate, pixelDistPerFrame);
            var theta = (nowFrame - oldFrame) * pixelDistPerFrame;
            var nv = v.normalized;
            var nextP = nowP + new Vector2(nv.x * theta, nv.y * theta);
            OpticalFlowCamerasController.NormalizePixelInRange(ref nextP.x, ref nextP.y, downsampleWidth, downsampleHeight);
            var newForward = panelVideoController.PixelToVector3(opticalFlowCamerasController.PixelToOriginPixelFloatValue(nextP, downsampleSize, originSize));
            mainNFOVCamera.transform.forward = newForward;
        }
        else{
            mainNFOVCamera.transform.forward = cameraNFOVs[nextCameraId].transform.forward;
            nextKeyFrame = -1;
            nowCameraId = nextCameraId;
            selectedDirection = -1;
        }
        oldFrame = nowFrame;
        UpdateLocator();
    }
    //Update locator display
    public void UpdateLocator() {
        if (locatorTexture == null)
            return;
        var videoWidth = videoPlayer.targetTexture.width;
        var videoHeight = videoPlayer.targetTexture.height;
        var width = (int)Mathf.Min(locatorTextureMaxLength, videoWidth * locatorTextureMaxLength / videoHeight);
        var height = (int)Mathf.Min(locatorTextureMaxLength, videoHeight * locatorTextureMaxLength / videoWidth);
        var size = new Vector2(width,height);
        var originSize = new Vector2(videoWidth, videoHeight);
        var angle = panelVideoController.EulerAngleToAngle(mainNFOVCamera.transform.eulerAngles);
        var pixel = opticalFlowCamerasController.PixelToOriginPixelFloatValue(panelVideoController.AngleToPixel(angle), originSize, size);
        int ox = (int)locatorPos.x;
        int oy = (int)locatorPos.y;
        int nx = (int)pixel.x;
        int ny = (int)pixel.y;
        foreach (var u in locatorShape) {
            int xx = (int)(ox + u.x);
            int yy = (int)(oy + u.y);
            OpticalFlowCamerasController.NormalizePixelInRange(ref xx, ref yy, width, height);
            locatorTexture.SetPixel(xx, height - 1 - yy, Color.clear);
        }
        foreach (var u in locatorShape)
        {
            int xx = (int)(nx + u.x);
            int yy = (int)(ny + u.y);
            OpticalFlowCamerasController.NormalizePixelInRange(ref xx, ref yy, width, height);
            locatorTexture.SetPixel(xx, height - 1 - yy, Color.red);
        }
        locatorTexture.Apply();
        locatorPos = new Vector2(nx, ny);
    }
    //Stop or drag the video progress bar
    public void VideoNowFrameChangedByUser() {
        oldFrame = (int)videoPlayer.frame;
        nextKeyFrame = -1;
        reachable = true;
    }
}
