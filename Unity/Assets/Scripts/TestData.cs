using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestData{
    public int downsampleWidth;
    public int downsampleHeight;
    public List<float[,]> pixelSaliency;
    
    public List<float[,]> regionalSaliency;
    public List<Vector2[,]> pixelOpticalFlow;
    public List<float[,]> pixelMask;
    public Rect[,] rectOfPixel;

    public List<Vector2>[] initialPath;
    public List<Vector2>[] fovAwarePath;
    public List<Vector2>[] smoothPath;

    public List<float[,]> downsamplePixelBlend;
    public List<Vector2[,]> downsamplePixelOpticalFlow;
    public List<float[,]> downsampleRegionalSaliency;

    public string videoURL;
    public void StoreData(Manager manager)
    {
        var opticalFlowCamerasController = manager.opticalFlowCamerasController;        
        opticalFlowCamerasController.GetDownsampleSize(out downsampleWidth, out downsampleHeight);
        pixelSaliency = opticalFlowCamerasController.pixelSaliency;
        regionalSaliency = opticalFlowCamerasController.regionalSaliency;
        pixelOpticalFlow = opticalFlowCamerasController.pixelOpticalFlow;
        pixelMask = opticalFlowCamerasController.pixelMaskList;
        rectOfPixel = opticalFlowCamerasController.rectOfPixel;
        initialPath = opticalFlowCamerasController.initialPath;
        fovAwarePath = opticalFlowCamerasController.fovAwarePath;
        smoothPath = opticalFlowCamerasController.smoothPath;
        Debug.Log("***storeData, smoothPath.length: "+smoothPath.Length);

        downsamplePixelBlend = opticalFlowCamerasController.GetDownsamplePixelBlendSaliencyMask(downsampleWidth, downsampleHeight, false);
        downsamplePixelOpticalFlow = opticalFlowCamerasController.GetDownsamplePixelOpticalFlow(downsampleWidth, downsampleHeight, true);
        List<float> maxRegionalSaliencyEveryFrame = null;
        downsampleRegionalSaliency = opticalFlowCamerasController.PreprocessRegionalSaliency(downsamplePixelBlend, downsampleWidth, downsampleHeight, ref maxRegionalSaliencyEveryFrame, false);


        videoURL = manager.videoPlayer.url;
    }
    public void RestoreData(Manager manager)
    {
        var opticalFlowCamerasController = manager.opticalFlowCamerasController;
        
        opticalFlowCamerasController.pixelSaliency = pixelSaliency;
        opticalFlowCamerasController.regionalSaliency = regionalSaliency;
        opticalFlowCamerasController.pixelOpticalFlow = pixelOpticalFlow;
        opticalFlowCamerasController.pixelMaskList = pixelMask;
        opticalFlowCamerasController.rectOfPixel = rectOfPixel;
        opticalFlowCamerasController.initialPath = initialPath;
        opticalFlowCamerasController.fovAwarePath = fovAwarePath;
        opticalFlowCamerasController.smoothPath = smoothPath;


        opticalFlowCamerasController.downsamplePixelBlend = downsamplePixelBlend;
        opticalFlowCamerasController.downsamplePixelOpticalFlow = downsamplePixelOpticalFlow;
        opticalFlowCamerasController.downsampleRegionalSaliency = downsampleRegionalSaliency;

        manager.videoPlayer.url = videoURL;
    }
}
