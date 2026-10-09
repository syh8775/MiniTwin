using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEditor.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace MiniTwin.Editor
{
    public static class MiniTwinBuilder
    {
        // [실제 코드에 남길 주석] 사용자가 만든 씬은 건드리지 않고 MiniTwin 전용 씬 3개를 생성합니다.
        [MenuItem("MiniTwin/Build prototype scenes")]
        public static void Build()
        {
            Folder("Assets/MiniTwin");Folder("Assets/MiniTwin/Scenes");Folder("Assets/MiniTwin/Fonts");
            Folder("Assets/MiniTwin/AR");Folder("Assets/MiniTwin/Settings");
            // [실제 코드에 남길 주석] OFL 라이선스와 함께 포함한 한글 폰트를 모든 환경에서 사용합니다.
            AssetDatabase.ImportAsset("Assets/MiniTwin/Fonts/NotoSansKR-Regular.otf",ImportAssetOptions.ForceSynchronousImport);
            Font font=AssetDatabase.LoadAssetAtPath<Font>("Assets/MiniTwin/Fonts/NotoSansKR-Regular.otf");
            if(font==null)throw new InvalidOperationException("Bundled Noto Sans KR font is missing.");
            var thresholds=AssetDatabase.LoadAssetAtPath<PumpThresholds>("Assets/MiniTwin/Settings/PumpThresholds.asset");
            if(thresholds==null){thresholds=ScriptableObject.CreateInstance<PumpThresholds>();AssetDatabase.CreateAsset(thresholds,"Assets/MiniTwin/Settings/PumpThresholds.asset");}
            var per=(XRGeneralSettingsPerBuildTarget)typeof(XRGeneralSettingsPerBuildTarget).GetMethod("GetOrCreate",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            if(!per.HasSettingsForBuildTarget(BuildTargetGroup.Standalone))per.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if(!per.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))per.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            var general=per.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            if(general!=null)
            {
                general.InitManagerOnStart=false;
                general.Manager.automaticLoading=false;general.Manager.automaticRunning=false;
                XRPackageMetadataStore.AssignLoader(general.Manager,"UnityEngine.XR.Simulation.SimulationLoader",BuildTargetGroup.Standalone);
                EditorUtility.SetDirty(general);EditorUtility.SetDirty(general.Manager);
            }
            foreach(string mode in new[]{"Dashboard","AR","VR"})
            {
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var light=new GameObject("Inspection Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.transform.eulerAngles=new Vector3(45,-30,0);
                RenderSettings.ambientLight=new Color(.4f,.5f,.57f);
                var root=new GameObject("MiniTwin App");var app=root.AddComponent<MiniTwinApp>();app.mode=mode;app.font=font;app.thresholds=thresholds;
                if(mode=="AR")SetupAR(app);
                if(mode=="VR")
                {
                    string sample="Assets/Samples/XR Interaction Toolkit/3.6.1/";
                    var rig=AssetDatabase.LoadAssetAtPath<GameObject>(sample+"Starter Assets/Prefabs/XR Origin (XR Rig).prefab");
                    var sim=AssetDatabase.LoadAssetAtPath<GameObject>(sample+"XR Interaction Simulator/XR Interaction Simulator.prefab");
                    if(rig==null||sim==null)throw new InvalidOperationException("XR samples not imported");
                    var rigObject=(GameObject)PrefabUtility.InstantiatePrefab(rig);
                    var origin=rigObject.GetComponent<XROrigin>();origin.transform.position=new Vector3(0,0,-1);
                    origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Device;origin.CameraYOffset=1.6f;
                    origin.CameraFloorOffsetObject.transform.localPosition=new Vector3(0,1.6f,0);origin.Camera.rect=new Rect(.16f,.22f,.55f,.66f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(origin);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(origin.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(origin.CameraFloorOffsetObject.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(origin.Camera);
                    PrefabUtility.InstantiatePrefab(sim);
                }
                EditorSceneManager.SaveScene(scene,"Assets/MiniTwin/Scenes/"+mode+".unity");
            }
            PlayerSettings.companyName="ShinYohan";PlayerSettings.productName="MiniTwin";PlayerSettings.runInBackground=true;
            PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/MiniTwin/Scenes/Dashboard.unity",true),new EditorBuildSettingsScene("Assets/MiniTwin/Scenes/AR.unity",true),new EditorBuildSettingsScene("Assets/MiniTwin/Scenes/VR.unity",true)};
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene("Assets/MiniTwin/Scenes/Dashboard.unity");
            Debug.Log("[MiniTwin] Dashboard, AR and VR scenes built.");
        }
        private static void Folder(string path)
        {if(!AssetDatabase.IsValidFolder(path))AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}

        private static void SetupAR(MiniTwinApp app)
        {
            var texture=new Texture2D(256,256,TextureFormat.RGB24,false);
            var rng=new System.Random(8775);
            for(int x=0;x<256;x++)for(int y=0;y<256;y++)
            {bool border=x<12||y<12||x>243||y>243;bool value=((x/16+y/16)%2==0)^((x/64+y/64)%3==1);texture.SetPixel(x,y,border?Color.black:value?Color.white:new Color(.08f,.34f,.28f));}
            texture.Apply();File.WriteAllBytes("Assets/MiniTwin/AR/PumpMarker.png",texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset("Assets/MiniTwin/AR/PumpMarker.png",ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath("Assets/MiniTwin/AR/PumpMarker.png");importer.isReadable=true;importer.SaveAndReimport();
            texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MiniTwin/AR/PumpMarker.png");
            var library=AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>("Assets/MiniTwin/AR/PumpImages.asset");
            if(library==null){library=ScriptableObject.CreateInstance<XRReferenceImageLibrary>();AssetDatabase.CreateAsset(library,"Assets/MiniTwin/AR/PumpImages.asset");}
            if(library.count==0)library.Add();library.SetTexture(0,texture,true);library.SetSpecifySize(0,true);library.SetSize(0,new Vector2(.5f,.5f));library.SetName(0,"PUMP-01");
            EditorUtility.SetDirty(library);

            var env=new GameObject("MiniTwin Simulation Room");
            var environmentType=typeof(UnityEngine.XR.Simulation.SimulationLoader).Assembly.GetType("UnityEngine.XR.Simulation.SimulationEnvironment");
            var environment=env.AddComponent(environmentType);var serialized=new SerializedObject(environment);
            serialized.FindProperty("m_CameraStartingPose.position").vector3Value=new Vector3(0,2,-4);
            serialized.FindProperty("m_CameraStartingPose.rotation").quaternionValue=Quaternion.Euler(15,0,0);
            serialized.FindProperty("m_CameraMovementBounds").boundsValue=new Bounds(new Vector3(0,2,0),new Vector3(16,8,16));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Simulation Floor";floor.transform.SetParent(env.transform);floor.transform.position=new Vector3(0,-.05f,0);floor.transform.localScale=new Vector3(10,.1f,10);
            var table=GameObject.CreatePrimitive(PrimitiveType.Cube);table.name="Marker Table";table.transform.SetParent(env.transform);table.transform.position=new Vector3(0,.45f,1);table.transform.localScale=new Vector3(4,.9f,2.5f);
            var marker=new GameObject("PUMP-01 virtual marker");marker.transform.SetParent(env.transform);marker.transform.position=new Vector3(0,.91f,1);marker.transform.eulerAngles=new Vector3(-90,0,0);
            var image=marker.AddComponent<UnityEngine.XR.Simulation.SimulatedTrackedImage>();var imageSettings=new SerializedObject(image);
            imageSettings.FindProperty("m_Image").objectReferenceValue=texture;imageSettings.FindProperty("m_ImagePhysicalSizeMeters").vector2Value=new Vector2(.5f,.5f);imageSettings.ApplyModifiedPropertiesWithoutUndo();
            GameObject prefab=PrefabUtility.SaveAsPrefabAsset(env,"Assets/MiniTwin/AR/SimulationRoom.prefab");UnityEngine.Object.DestroyImmediate(env);
            var prefType=typeof(UnityEngine.XR.Simulation.SimulationLoader).Assembly.GetType("UnityEngine.XR.Simulation.XRSimulationPreferences");
            var preferences=(UnityEngine.Object)prefType.GetProperty("Instance",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.FlattenHierarchy).GetValue(null,null);
            var prefs=new SerializedObject(preferences);prefs.FindProperty("m_EnvironmentPrefab").objectReferenceValue=prefab;prefs.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(preferences);

            var session=new GameObject("AR Session").AddComponent<ARSession>();session.enabled=false;session.gameObject.AddComponent<ARInputManager>().enabled=false;
            var originObject=new GameObject("AR XR Origin");var origin=originObject.AddComponent<XROrigin>();
            var offset=new GameObject("Camera Offset");offset.transform.SetParent(originObject.transform,false);origin.CameraFloorOffsetObject=offset;
            var camObject=new GameObject("Main Camera");camObject.tag="MainCamera";camObject.transform.SetParent(offset.transform,false);
            var camera=camObject.AddComponent<Camera>();camera.nearClipPlane=.05f;camera.farClipPlane=100;origin.Camera=camera;
            camObject.AddComponent<AudioListener>();camObject.AddComponent<ARCameraManager>().enabled=false;camObject.AddComponent<ARCameraBackground>().enabled=false;camera.rect=new Rect(.16f,.22f,.55f,.66f);
            var pose=camObject.AddComponent<TrackedPoseDriver>();
            pose.positionInput=new InputActionProperty(new InputAction("Position",InputActionType.Value,"<HandheldARInputDevice>/devicePosition"));
            pose.rotationInput=new InputActionProperty(new InputAction("Rotation",InputActionType.Value,"<HandheldARInputDevice>/deviceRotation"));
            var images=originObject.AddComponent<ARTrackedImageManager>();images.referenceLibrary=library;images.requestedMaxNumberOfMovingImages=1;images.enabled=false;
            var bridge=app.gameObject.AddComponent<MiniTwinAR>();bridge.app=app;bridge.images=images;bridge.manager=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone).Manager;
            // [실제 코드에 남길 주석] AR 카메라 배경을 URP renderer feature로 연결합니다.
            foreach(string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);if(!path.StartsWith("Assets/Settings/"))continue;
                var data=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(path);
                bool found=false;foreach(var feature in data.rendererFeatures)if(feature is ARBackgroundRendererFeature)found=true;
                if(!found){var feature=ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();feature.name="AR Camera Background";AssetDatabase.AddObjectToAsset(feature,data);data.rendererFeatures.Add(feature);EditorUtility.SetDirty(data);}
            }
        }
    }
}
