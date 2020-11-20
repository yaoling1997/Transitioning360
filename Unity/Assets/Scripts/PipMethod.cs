using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.EventSystems;

public class PipMethod : MonoBehaviour {
    private readonly static float oo = 1e9f;
    private readonly static float eps = 1e-3f;
    private Manager manager;
    private OpticalFlowCamerasController opticalFlowCamerasController;
    private PanelVideoController panelVideoController;
    private Camera mainCamera;
    private VideoPlayer videoPlayer;

    public Canvas canvas;
    public float pipSizeMultiple;
    public float pipWidthWeight;
    public float pipHeightWeight;
    public float minHorizentalAngle;
    public float minVerticalAngle;
    public float maxTilt;
    public float maxDepth;
    public float minDepth;
    public int downsampleWidth;
    public int downsampleHeight;
    public bool canPauseUpdate;
    public List<float[,]> downsamplePixelBlend;
    public List<Vector2[,]> downsamplePixelOpticalFlow;
    public List<Vector2>[] smoothPath;
    public GameObject pipPrefab;
    public GameObject[] pipList;

    private long savedTime;

    public bool isReady;

    private void Awake()
    {
        manager = GameObject.Find("Manager").GetComponent<Manager>();
        opticalFlowCamerasController = manager.opticalFlowCamerasController;
        panelVideoController = manager.panelVideoController;
        videoPlayer = manager.videoPlayer;
        mainCamera = manager.mainCamera;
        isReady = false;
        pipSizeMultiple = 0.3f;
        pipWidthWeight = 1;
        pipHeightWeight = 1;
        minHorizentalAngle = 40;
        minVerticalAngle = 40;
        maxTilt = 120;
        maxDepth = 50;
        minDepth = -20;
        canPauseUpdate = true;
        var rt = canvas.GetComponent<RectTransform>();
        var canvasSize = rt.sizeDelta;
    }
    // Use this for initialization
    void Start () {

    }

    // Update is called once per frame
    void Update()
    {
        if (isReady) {
            if (pipList != null && pipList.Length > 0)
            {
                foreach (var item in pipList)
                {
                    var rt = item.GetComponent<RectTransform>();
                }
            }
            UpdatePips();
            mainCamera.Render();
            var scrollWheelV = Input.GetAxis("Mouse ScrollWheel");
            if (scrollWheelV != 0)
            {
                var newMultiple = pipSizeMultiple + 0.01f * scrollWheelV / Mathf.Abs(scrollWheelV);
                var delta = 0.01f * scrollWheelV / Mathf.Abs(scrollWheelV);
                Debug.Log(string.Format("scrollWheelV:{0}, newMultiple:{1}, delta:{2}", scrollWheelV, newMultiple, delta));
                pipSizeMultiple = Mathf.Clamp(newMultiple, 0.1f, 0.99f);
                ResizePipsSize();
            }
        }
    }
    public Vector2 GetNewPipWindowSize() {
        var canvasSize = canvas.GetComponent<RectTransform>().sizeDelta;        
        float h = canvasSize.y * pipSizeMultiple;
        float w = h * pipWidthWeight / pipHeightWeight;
        var newSize = new Vector2(w, h);
        return newSize;
        
    }
    public void ResizePipsSize() {
        if (pipList == null)
            return;        
        var newSize = GetNewPipWindowSize();
        int camNum = panelVideoController.cameraGroupNum;
        Debug.Log(string.Format("newSize: {0}, pipSizeMultiple: {1}", newSize, pipSizeMultiple));
        for (int camId = 0; camId < camNum;camId++) {
            var tmp = pipList[camId];
            var rt = tmp.GetComponent<RectTransform>();
            rt.sizeDelta = newSize;
        }
    }
    public void Init() {
        Clear();
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        downsamplePixelBlend = opticalFlowCamerasController.downsamplePixelBlend;
        downsamplePixelOpticalFlow = opticalFlowCamerasController.downsamplePixelOpticalFlow;
        smoothPath = opticalFlowCamerasController.smoothPath;        
        int camNum = panelVideoController.cameraGroupNum;
        pipList = new GameObject[camNum];                
        var canvasRt = canvas.GetComponent<RectTransform>();
        for (int camId = 0; camId < camNum; camId++) {
            var tmp = Instantiate(pipPrefab);
            var pipController = tmp.GetComponent<PipController>();
            tmp.name = "pip" + camId;
            pipController.id = camId;
            var rt = tmp.GetComponent<RectTransform>();
            //Debug.Log("init scale: " + rt.localScale);
            tmp.transform.SetParent(canvas.transform);
            //Debug.Log("init scale after set parent: " + rt.localScale);
            rt.localPosition= Vector3.zero;
            //Debug.Log("init scale after set localPosition: " + rt.localScale);
            rt.localEulerAngles = Vector3.zero;
            //Debug.Log("init scale after set localEulerAngles: " + rt.localScale);
            rt.SetAsFirstSibling();
            //Debug.Log("init scale after SetAsFirstSibling: " + rt.localScale);
            rt.localScale = new Vector3(1, 1, 1);
            //Debug.Log("init scale after set localScale: " + rt.localScale);
            var ri = tmp.GetComponent<RawImage>();
            if (panelVideoController.cameraNFOVs[camId].targetTexture != null) {
                var tmpTexture = panelVideoController.cameraNFOVs[camId].targetTexture;
                var width = manager.cameraCalculate.targetTexture.width;
                var height = (int)Mathf.Round(width * pipHeightWeight / pipWidthWeight);
                panelVideoController.cameraNFOVs[camId].targetTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                Debug.Log(string.Format("pip width:{0}, height:{1}", width, height));
                Destroy(tmpTexture);
            }
            ri.texture = panelVideoController.cameraNFOVs[camId].targetTexture;
            pipList[camId] = tmp;
            Debug.Log("canvasRt.sizeDelta: " + canvasRt.sizeDelta);
            var anchoredPos = new Vector2(canvasRt.sizeDelta.x, canvasRt.sizeDelta.y);
            rt.anchoredPosition = anchoredPos;
            Debug.Log("rt.positon: " + rt.position);
        }
        ResizePipsSize();
        isReady = true;
    }

    public void Clear() {
        if (pipList != null)
        {
            foreach (var item in pipList)
                Destroy(item);
            pipList = null;
        }
        isReady = false;
    }

    public int GetCurrentKeyFrame()
    {
        if (opticalFlowCamerasController.pixelSaliency == null)
        {
            return 0;
        }
        int totalKeyFrame = opticalFlowCamerasController.pixelSaliency.Count;
        return (int)(Mathf.Min(videoPlayer.frame, videoPlayer.frameCount - 1) * totalKeyFrame / videoPlayer.frameCount);
    }

    public void VideoChangedByUser() {
        var beginTime = DateTime.Now.Ticks;                
        Update();
        var endTime = DateTime.Now.Ticks;
        Debug.Log("time of pip update: " + ((endTime - beginTime) / 1e7).ToString("F5"));
    }

    public void UpdatePips() {
        var nextSavedTime = DateTime.Now.Ticks;        
        //Debug.Log("time between two UpdatePips: " + (nextSavedTime - savedTime) / 1e7);
        savedTime = DateTime.Now.Ticks;
        if (!isReady|| pipList==null || (!canPauseUpdate&&!videoPlayer.isPlaying))
            return;
                
        int camNum = panelVideoController.cameraGroupNum;
        for (int camId = 0; camId < camNum; camId++) {
            var subCam = panelVideoController.cameraNFOVs[camId];
            //UpdateCamera(smoothPath[camId], subCam);
            var mainCameraAngle = panelVideoController.EulerAngleToAngle(mainCamera.transform.eulerAngles);
            var subCamAngle = panelVideoController.EulerAngleToAngle(subCam.transform.eulerAngles);
            var deltaAngle = opticalFlowCamerasController.GetVector2Of2Pixels(mainCameraAngle, subCamAngle, 360);
            if (Mathf.Abs(deltaAngle.x) <= minHorizentalAngle && Mathf.Abs(deltaAngle.y) <= minVerticalAngle)
            {
                UpdatePipShowStatus(camId, true);                
            }
            else {
                UpdatePipShowStatus(camId, false);                
                //change pos
                var pipCenterPos = UpdatePipPos(subCam, pipList[camId]);
                //change rotation
                var alpha = UpdatePipRotation(subCam, pipList[camId], pipCenterPos);
                var rt = pipList[camId].GetComponent<RectTransform>();                
                //change tilt       
                UpdatePipTilt(subCam, pipList[camId], pipCenterPos, alpha);                                
                //change depth        
                UpdatePipDepth(subCam, pipList[camId]);
            }
        }
    }

    public void UpdatePipShowStatus(int camId, bool ifclose) {
        var pip = pipList[camId];
        if (ifclose || manager.panoramaVideoController.multiMethod.IfCameraWindowClosed(camId))
        {
            pip.SetActive(false);
        }
        else {
            pip.SetActive(true);
        }
    }

    public Vector2 UpdatePipPos(Camera subCam,GameObject pip) {
        var mainCameraAngle = panelVideoController.EulerAngleToAngle(mainCamera.transform.eulerAngles);
        var subCamAngle = panelVideoController.EulerAngleToAngle(subCam.transform.eulerAngles);
        var deltaAngle = opticalFlowCamerasController.GetVector2Of2Pixels(mainCameraAngle, subCamAngle, 360);
        var canvasSize = canvas.GetComponent<RectTransform>().sizeDelta;
        var pipSize = pip.GetComponent<RectTransform>().sizeDelta;
        var canvasCenter = canvasSize / 2;
        var pipCenterPos = Vector2.zero;
        if (Dcmp(deltaAngle.x) == 0)
        {
            pipCenterPos = new Vector2(canvasSize.x / 2, subCamAngle.y > mainCameraAngle.y ? canvasSize.y - pipSize.y / 2 : pipSize.y / 2);
        }
        else if (Dcmp(deltaAngle.y) == 0)
        {
            pipCenterPos = new Vector2(subCamAngle.x > mainCameraAngle.x ? canvasSize.x - pipSize.x / 2 : pipSize.x / 2, canvasSize.y / 2);
        }
        else
        {
            float tanTheta = deltaAngle.y / deltaAngle.x;
            float x = deltaAngle.x > 0 ? canvasSize.x - pipSize.x / 2 : pipSize.x / 2;
            float y = deltaAngle.y > 0 ? canvasSize.y - pipSize.y / 2 : pipSize.y / 2;
            var p1 = new Vector2(x, (x - canvasSize.x / 2) * tanTheta + canvasSize.y / 2);
            var p2 = new Vector2((y - canvasSize.y / 2) / tanTheta + canvasSize.x / 2, y);
            pipCenterPos = (p1 - canvasCenter).magnitude < (p2 - canvasCenter).magnitude ? p1 : p2;
        }
        var rt = pip.GetComponent<RectTransform>();
        rt.localPosition = Vector3.zero;
        rt.anchoredPosition = new Vector2(pipCenterPos.x, pipCenterPos.y);
        //Debug.Log("UpdatePipPos rt.positon: " + rt.position);
        return pipCenterPos;
        //Debug.Log(string.Format("camId:{0}, anchoredPosition: {1}", camId, rt.anchoredPosition));
    }
    public float UpdatePipRotation(Camera subCam, GameObject pip, Vector2 pipCenterPos) {
        var mainCameraAngle = panelVideoController.EulerAngleToAngle(mainCamera.transform.eulerAngles);
        var subCamAngle = panelVideoController.EulerAngleToAngle(subCam.transform.eulerAngles);
        var deltaAngle = opticalFlowCamerasController.GetVector2Of2Pixels(mainCameraAngle, subCamAngle, 360);
        var canvasSize = canvas.GetComponent<RectTransform>().sizeDelta;
        var pipSize = pip.GetComponent<RectTransform>().sizeDelta;
        var canvasCenter = canvasSize / 2;
        var rt = pip.GetComponent<RectTransform>();  
        var newLocalEulerAngles= Vector3.zero;

        var v = pipCenterPos - canvasCenter;
        var tmp = Vector2.Dot(v, new Vector2(1, 0)) / v.magnitude;
        float theta = 0;
        if (v.y >= 0)
        {
            theta = Mathf.Acos(tmp) * Mathf.Rad2Deg;
        }
        else {
            theta = 360 - Mathf.Acos(tmp) * Mathf.Rad2Deg;
        }
        newLocalEulerAngles = new Vector3(0, 0, -(180 - theta));
            //Debug.Log(string.Format("UpdatePipRotation theta:{0}", theta));                
        var alpha = newLocalEulerAngles.z;
        rt.localEulerAngles = newLocalEulerAngles;
        var oldEulerAngles = subCam.transform.eulerAngles;
        subCam.transform.eulerAngles = new Vector3(oldEulerAngles.x, oldEulerAngles.y, newLocalEulerAngles.z);
        return alpha;
        //Debug.Log(string.Format("camId:{0}, anchoredPosition: {1}", camId, rt.anchoredPosition));
    }
    public float GetTilt(float dist) {
        return maxTilt + (0 - maxTilt) * (GetMaxDist() - dist);
    }
    public float GetTilt(Vector2 deltaAngle) {
        return GetTilt(GetNormalizedEuclideanDistance(deltaAngle));
    }
    public void UpdatePipTilt(Camera subCam, GameObject pip, Vector2 pipCenterPos, float alpha)
    {
        var mainCameraAngle = panelVideoController.EulerAngleToAngle(mainCamera.transform.eulerAngles);
        var subCamAngle = panelVideoController.EulerAngleToAngle(subCam.transform.eulerAngles);
        var deltaAngle = opticalFlowCamerasController.GetVector2Of2Pixels(mainCameraAngle, subCamAngle, 360);
        var canvasSize = canvas.GetComponent<RectTransform>().sizeDelta;
        var pipSize = pip.GetComponent<RectTransform>().sizeDelta;
        var canvasCenter = canvasSize / 2;
        var rt = pip.GetComponent<RectTransform>();        

        var P = new Vector3(pipCenterPos.x, pipCenterPos.y, 0);

        var A_ = Vector3.zero;
        var B_ = Vector3.zero;
        var A = Vector3.zero;
        var B = Vector3.zero;

        if (Dcmp(pipCenterPos.x - pipSize.x / 2) == 0)
        {
            A = new Vector3(0, pipCenterPos.y - pipSize.y / 2, 0);
            B = new Vector3(0, pipCenterPos.y + pipSize.y / 2, 0);
        }
        else if (Dcmp(pipCenterPos.x - (canvasSize.x - pipSize.x / 2)) == 0)
        {
            //A = new Vector3(canvasSize.x, pipCenterPos.y + pipSize.y / 2, 0);
            //B = new Vector3(canvasSize.x, pipCenterPos.y - pipSize.y / 2, 0);
            A = new Vector3(canvasSize.x - pipSize.x, pipCenterPos.y - pipSize.y / 2, 0);
            B = new Vector3(canvasSize.x - pipSize.x, pipCenterPos.y + pipSize.y / 2, 0);
        }
        else if (Dcmp(pipCenterPos.y - pipSize.y / 2) == 0)
        {
            //A = new Vector3(pipCenterPos.x - pipSize.x / 2, canvasSize.y, 0);
            //B = new Vector3(pipCenterPos.x + pipSize.x / 2, canvasSize.y, 0);
            A = new Vector3(pipCenterPos.x - pipSize.x / 2, 0, 0);
            B = new Vector3(pipCenterPos.x - pipSize.x / 2, pipSize.y, 0);

        }
        else if (Dcmp(pipCenterPos.y - (canvasSize.y - pipSize.y / 2)) == 0)
        {
            //A = new Vector3(pipCenterPos.x + pipSize.x / 2, 0, 0);
            //B = new Vector3(pipCenterPos.x - pipSize.x / 2, 0, 0);
            A = new Vector3(pipCenterPos.x - pipSize.x / 2, canvasSize.y - pipSize.y, 0);
            B = new Vector3(pipCenterPos.x - pipSize.x / 2, canvasSize.y, 0);

        }
        else {
            Debug.LogError("tilt not in the case!");
        }        

        var PA = A - P;
        var PB = B - P;
        Quaternion rotation = Quaternion.Euler(0, 0, alpha);
        Matrix4x4 m = Matrix4x4.Rotate(rotation);
        var PA_ = m.MultiplyPoint3x4(PA);
        var PB_ = m.MultiplyPoint3x4(PB);
        A_ = P + PA_;
        B_ = P + PB_;
        var tiltAngle = GetTilt(deltaAngle);
        var AViewport = new Vector2(A_.x / canvasSize.x, A_.y / canvasSize.y);
        var BViewport = new Vector2(B_.x / canvasSize.x, B_.y / canvasSize.y);
        var realA_ = mainCamera.ViewportToWorldPoint(new Vector3(AViewport.x, AViewport.y, canvas.planeDistance));
        var realB_ = mainCamera.ViewportToWorldPoint(new Vector3(BViewport.x, BViewport.y, canvas.planeDistance));
        tiltAngle = Mathf.Clamp(tiltAngle, 0, maxTilt);
        rt.RotateAround(realA_, realB_ - realA_, -tiltAngle);
        //Debug.Log("after rotation rt.position: " + rt.transform.position);
        //Debug.Log(string.Format("camId:{0}, anchoredPosition: {1}", camId, rt.anchoredPosition));
    }
    public float GetDepth(float dist)
    {
        return minDepth + (maxDepth - minDepth) * (GetMaxDist() - dist);
    }
    public float GetDepth(Vector2 deltaAngle)
    {
        return GetDepth(GetNormalizedEuclideanDistance(deltaAngle));
    }
    public void UpdatePipDepth(Camera subCam, GameObject pip)
    {
        var mainCameraAngle = panelVideoController.EulerAngleToAngle(mainCamera.transform.eulerAngles);
        var subCamAngle = panelVideoController.EulerAngleToAngle(subCam.transform.eulerAngles);
        var deltaAngle = opticalFlowCamerasController.GetVector2Of2Pixels(mainCameraAngle, subCamAngle, 360);
        var canvasSize = canvas.GetComponent<RectTransform>().sizeDelta;
        var pipSize = pip.GetComponent<RectTransform>().sizeDelta;
        var canvasCenter = canvasSize / 2;
        var rt = pip.GetComponent<RectTransform>();
        var depth = GetDepth(deltaAngle);
        rt.position = rt.position + mainCamera.transform.forward * depth;
    }
    int Dcmp(float x) {
        if (Mathf.Abs(x) < eps)
            return 0;
        return x < 0 ? -1 : 1;
    }
    public float GetNormalizedEuclideanDistance(Vector2 v) {
        var x = v.x / 180;
        var y = v.y / 180;
        return Mathf.Sqrt((x * x + y * y) / 2);
        //return Mathf.Sqrt(x * x + y * y);

    }
    public float GetMaxDist() {
        return GetNormalizedEuclideanDistance(new Vector2(180, 180));
        //return 1;
    }
}
