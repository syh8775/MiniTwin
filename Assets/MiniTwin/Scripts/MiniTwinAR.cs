using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using System.Collections;

namespace MiniTwin
{
    // [실제 코드에 남길 주석] 추적 소실 시 마지막으로 확인한 위치를 유지하고, 재감지 시 위치를 갱신합니다.
    public sealed class MiniTwinAR : MonoBehaviour
    {
        public ARTrackedImageManager images;
        public XRManagerSettings manager;
        public MiniTwinApp app;
        public bool Tracking { get; private set; }
        public int TrackedCount { get; private set; }
        private bool hasTrackedPose;
        private IEnumerator Start()
        {
            if(manager==null){app.TrackingStatus="XR manager not assigned";yield break;}
            yield return manager.InitializeLoader();
            if(manager.activeLoader==null){app.TrackingStatus="XR Simulation 로더 시작 실패";yield break;}
            manager.StartSubsystems();
            FindAnyObjectByType<ARSession>(FindObjectsInactive.Include).enabled=true;
            FindAnyObjectByType<ARInputManager>().enabled=true;
            images.enabled=true;
            FindAnyObjectByType<ARCameraManager>().enabled=true;
            FindAnyObjectByType<ARCameraBackground>().enabled=true;
        }
        private void Update()
        {
            if(images==null||app==null||app.View==null)return;
            ARTrackedImage selected=null;TrackedCount=0;
            foreach(var image in images.trackables)
            {
                TrackedCount++;
                if(image.trackingState==TrackingState.Tracking)selected=image;
            }
            Tracking=selected!=null;
            if(Tracking)hasTrackedPose=true;
            app.TrackingStatus=Tracking?"가상 마커 추적 중":hasTrackedPose?"추적 소실 · 마지막 확인 위치 유지":"가상 마커를 찾는 중";
            app.View.gameObject.SetActive(hasTrackedPose);
            if(Tracking)
            {
                app.View.transform.position=selected.transform.position;
                app.View.transform.rotation=Quaternion.Euler(0,selected.transform.eulerAngles.y,0);
            }
        }
        private void OnDestroy()
        {
            if(manager!=null)
            {
                if(manager.activeLoader!=null){manager.StopSubsystems();manager.DeinitializeLoader();}
            }
        }
    }
}
