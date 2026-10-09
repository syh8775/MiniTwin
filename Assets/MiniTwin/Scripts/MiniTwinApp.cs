using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MiniTwin
{
    [DefaultExecutionOrder(-31000)]
    public sealed partial class MiniTwinApp : MonoBehaviour
    {
        public string mode = "Dashboard";
        public Font font;
        public PumpThresholds thresholds;
        public MiniTwinCore Core { get; private set; }
        public PumpView View { get; private set; }
        public string TrackingStatus { get; set; } = "가상 마커를 찾는 중";
        private Text status, feedback, snapshot, training, result, tracking, trainingInstruction;
        private readonly RectTransform[] trainingActionGroups=new RectTransform[7];
        private readonly Text[] trainingStepTexts=new Text[7];
        private readonly Button[] scenarioButtons=new Button[3];
        private readonly GameObject[] worldActionGroups=new GameObject[7];
        private Text worldStage, worldStepCounter, worldHint, repairHint;
        private GameObject worldScenarios;
        private Image worldProgress;
        private RectTransform worldCanvas;
        private Sprite worldCardSprite;
        private GameObject worldRepairTray, worldTraySurface, worldTrayGuide;
        private readonly GameObject[] worldReplacementParts=new GameObject[3];
        private bool repairTrayVisible;
        private Text[] sensorTexts = new Text[4];
        private SensorGraph[] graphs = new SensorGraph[4];
        private RectTransform canvasRoot;
        private Camera orbitCamera;
        private UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRInteractionSimulator vrSimulator;
        private UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule vrInputModule;
        private readonly System.Collections.Generic.List<RaycastResult> uiHits=new System.Collections.Generic.List<RaycastResult>();
        private UnityEngine.XR.Management.XRManagerSettings modeManager;
        private System.Collections.Generic.List<UnityEngine.XR.Management.XRLoader> originalLoaders;
        private float yaw = 38, pitch = 22, distance = 6.5f;
        private readonly Color bg = new Color(.045f,.071f,.09f);
        private readonly Color panel = new Color(.08f,.12f,.15f);
        private readonly Color accent = new Color(.24f,.83f,.72f);
        private readonly Color foreground = new Color(.89f,.94f,.96f);
        private readonly Color muted = new Color(.55f,.66f,.71f);

        private void Awake()
        {
#if UNITY_EDITOR
            if(UnityEngine.XR.Management.XRGeneralSettings.Instance!=null)
            {
                modeManager=UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager;
                originalLoaders=new System.Collections.Generic.List<UnityEngine.XR.Management.XRLoader>(modeManager.activeLoaders);
                if(mode!="AR")modeManager.TrySetLoaders(new System.Collections.Generic.List<UnityEngine.XR.Management.XRLoader>());
            }
#endif

            // [실제 코드에 남길 주석] 씬이 바뀌어도 동일 코어를 유지하며 정적 전역 싱글턴을 만들지 않습니다.
            Core = FindAnyObjectByType<MiniTwinCore>();
            if (Core == null)
            {
                var root = new GameObject("MiniTwin Shared Core");
                Core = root.AddComponent<MiniTwinCore>(); Core.thresholds = thresholds;
                DontDestroyOnLoad(root);
            }
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject model = new GameObject("Pump-01");
            model.transform.position = mode == "VR" ? new Vector3(0,.6f,3) : Vector3.zero;
            View = model.AddComponent<PumpView>(); View.core = Core; View.xrInteractions = mode == "VR"; View.Build();
            if (mode == "AR") model.SetActive(false);
            if (mode == "Dashboard")
            {
                var cameraObject = new GameObject("Main Camera"); cameraObject.tag = "MainCamera";
                orbitCamera = cameraObject.AddComponent<Camera>(); orbitCamera.backgroundColor = bg;
                orbitCamera.clearFlags = CameraClearFlags.SolidColor;
                orbitCamera.fieldOfView = 45; orbitCamera.rect = viewerRect;
                cameraObject.AddComponent<AudioListener>(); PositionCamera();
                CreateFloor(new Vector3(0,-.1f,0), false);
            }
            if (mode == "VR") { CreateFloor(new Vector3(0,-.05f,3),true); CreateVRStation(); }
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem"); events.AddComponent<EventSystem>();
                if(mode=="VR")events.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
                else events.AddComponent<InputSystemUIInputModule>();
            }
            BuildUI(); Core.Changed += Refresh; Refresh();
            if(mode=="VR")
            {
                vrSimulator=FindAnyObjectByType<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRInteractionSimulator>();
                vrInputModule=FindAnyObjectByType<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
            }
        }

        private System.Collections.IEnumerator Start()
        {
            // SDK의 sceneLoaded 오프셋 복구가 끝난 뒤 PC 시뮬레이터의 초기 시점을 적용합니다.
            if(mode=="VR")
            {
                var origin=FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
                origin.CameraYOffset=1.6f;
                origin.Camera.rect=viewerRect;
                origin.CameraFloorOffsetObject.transform.localPosition=new Vector3(0,1.6f,0);
            }
            yield return null;
            ConfigureView();
            WarmText();
            foreach(var system in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                if(system.GetComponents<BaseInputModule>().Length==0)Destroy(system.gameObject);
        }

        private void CreateFloor(Vector3 position, bool teleport)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Inspection Floor";
            floor.transform.position = position; floor.transform.localScale = new Vector3(11,.1f,11);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = new Color(.095f,.13f,.15f);
            floor.GetComponent<Renderer>().sharedMaterial = material;
            if (teleport)
            {
                var area = floor.AddComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
                area.interactionLayers = new InteractionLayerMask { value = 1 << 31 };
            }
        }

        public void SwitchMode(string next)
        {
            if(next==mode)return;
            if(!Application.isEditor && next!="Dashboard") { Core.Inspect("Editor에서 AR·VR 씬을 실행하세요"); return; }
            SceneManager.LoadScene(next);
        }
        // [실제 코드에 남길 주석] XR 조작도 화면 UI와 같은 단계만 노출하며 한 조작판에서 선택합니다.
        private void CreateVRStation()
        {
            if(FindAnyObjectByType<XRInteractionManager>()==null)new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var bench=GameObject.CreatePrimitive(PrimitiveType.Cube);bench.name="Inspection Bench";bench.transform.position=new Vector3(0,.5f,3);bench.transform.localScale=new Vector3(3.8f,.2f,1.7f);
            var board=new GameObject("XR Training Console");board.transform.position=new Vector3(-2.7f,1.45f,3.2f);
            board.transform.rotation=Quaternion.identity;
            var backing=GameObject.CreatePrimitive(PrimitiveType.Cube);backing.name="XR Console Backing";backing.transform.SetParent(board.transform,false);
            backing.transform.localScale=new Vector3(2.07f,1.748f,.025f);backing.transform.localPosition=new Vector3(0,0,.03f);backing.GetComponent<Renderer>().enabled=false;
            CreateWorldConsoleSurface(board.transform);
            for(int i=0;i<7;i++)
            {
                var group=new GameObject("XR Training Step "+i,typeof(RectTransform));group.transform.SetParent(worldCanvas,false);
                SetRect(group.GetComponent<RectTransform>(),Vector2.zero,Vector2.one);worldActionGroups[i]=group;
            }
            string[] actions={"ppe",null,null,"stop","isolate","repair","restart"};
            string[] objectNames={"XR PPE",null,null,"XR STOP","XR ISOLATE","XR REPAIR","XR RESTART"};
            for(int i=0;i<7;i++)
            {
                if(actions[i]==null||i==5)continue;
                string action=actions[i];
                WorldButton(worldActionGroups[i].transform,objectNames[i],MiniTwinCore.StepNames[i],0,()=>Core.Act(action,MiniTwinCore.PartFor(Core.Fault)));
            }
            string[] partIds={"Motor","Bearing","Pipe"};
            for(int i=0;i<3;i++)
            {
                string part=partIds[i];
                WorldButton(worldActionGroups[1].transform,"XR Inspect "+part,InspectionLabel(i),i,()=>Core.Inspect(part));
                FaultType cause=(FaultType)(i+1);
                WorldButton(worldActionGroups[2].transform,"XR Cause "+cause,MiniTwinCore.CauseName(cause),i,()=>Core.ChooseCause(cause));
            }
            // 시나리오 선택부터 완료까지 이 3D 조작판 하나에서 진행합니다.
            worldScenarios=new GameObject("VR Scenario Choices",typeof(RectTransform));worldScenarios.transform.SetParent(worldCanvas,false);SetRect(worldScenarios.GetComponent<RectTransform>(),Vector2.zero,Vector2.one);
            for(int i=0;i<3;i++){FaultType fault=(FaultType)(i+1);WorldButton(worldScenarios.transform,"VR Scenario "+fault,MiniTwinCore.FaultName(fault),i,()=>Core.BeginTraining(fault));}
            worldRepairTray=new GameObject("XR Repair Tray");worldRepairTray.transform.position=new Vector3(2.1f,1.25f,3);
            var tray=GameObject.CreatePrimitive(PrimitiveType.Cube);worldTraySurface=tray;tray.name="Replacement Tray";tray.transform.SetParent(worldRepairTray.transform,false);tray.transform.localScale=new Vector3(1.8f,.08f,.95f);
            var trayMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));trayMaterial.color=panel;tray.GetComponent<Renderer>().sharedMaterial=trayMaterial;
            CreateTrayGuide();
            string[] parts={"Fan","Bearing","Pipe"};
            for(int i=0;i<3;i++)
            {
                var part=GameObject.CreatePrimitive(PrimitiveType.Cube);worldReplacementParts[i]=part;part.name="Replacement "+parts[i];part.transform.SetParent(worldRepairTray.transform,false);
                part.transform.localPosition=new Vector3(-.55f+i*.55f,.2f,.23f);part.transform.localScale=Vector3.one*.18f;
                var data=part.AddComponent<PumpPart>();data.id=parts[i];string partId=parts[i];
                // 이름표와 부품을 함께 누를 수 있게 선택 영역을 넓힙니다. 운반 없이 선택으로 판정합니다.
                var hit=part.GetComponent<BoxCollider>();hit.size=new Vector3(2.85f,3,1.1f);hit.center=new Vector3(0,.6f,0);
                var choice=part.AddComponent<XRSimpleInteractable>();choice.selectEntered.AddListener(_=>Core.Act("repair",partId));
                CreatePartPlate(part.transform,MiniTwinCore.PartLabel(parts[i]));
            }
            worldTraySurface.SetActive(false);worldTrayGuide.SetActive(false);foreach(var part in worldReplacementParts)part.SetActive(false);
            RefreshWorldStation();
        }

        // [실제 코드에 남길 주석] 카드 전체를 같은 평면에 그려 원근으로 생기는 좌우 여백 차이를 없앱니다.

        private void CreateWorldConsoleSurface(Transform parent)
        {
            var obj=new GameObject("XR Console Surface",typeof(RectTransform),typeof(Canvas));obj.transform.SetParent(parent,false);
            worldCanvas=obj.GetComponent<RectTransform>();worldCanvas.sizeDelta=new Vector2(SidePanelWidth*WorldUnit,768);worldCanvas.localScale=Vector3.one*.0023f;worldCanvas.localPosition=new Vector3(0,0,-.07f);
            var canvas=obj.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=Camera.main;canvas.sortingOrder=5;
            // 공간 UI는 같은 역할의 화면 치수를 두 배의 Canvas 단위로 환산합니다. 입력은 XR Collider가 유지합니다.
            worldCardSprite=CreateWorldCardSprite();
            var shadow=WorldCard(worldCanvas,"Console Shadow",Vector2.zero,Vector2.one,new Color(0,0,0,.24f));shadow.offsetMin=new Vector2(8,-8);shadow.offsetMax=new Vector2(8,-8);
            WorldCard(worldCanvas,"Console Border",Vector2.zero,Vector2.one,new Color(.19f,.27f,.31f));
            var fill=WorldCard(worldCanvas,"Console Fill",Vector2.zero,Vector2.one,new Color(.045f,.072f,.094f));fill.offsetMin=Vector2.one*4;fill.offsetMax=-Vector2.one*4;
            TextAt(worldCanvas,"정비 훈련  ·  PUMP-01",CaptionSize*2,Padding*WorldUnit,32,560,48,muted);
            worldStepCounter=TextAt(worldCanvas,"",CaptionSize*2,656,32,208,48,accent);worldStepCounter.alignment=TextAnchor.MiddleRight;
            worldStage=TextAt(worldCanvas,"",HeadingSize*2,Padding*WorldUnit,88,832,80,foreground);
            var track=Box(worldCanvas,"Step Progress",Vector2.zero,Vector2.one,new Color(.14f,.21f,.25f));SetRow(track,0,1,184,8,Padding*WorldUnit,Padding*WorldUnit);
            var progress=Box(track,"Completed Progress",Vector2.zero,new Vector2(0,1),accent);worldProgress=progress.GetComponent<Image>();worldProgress.raycastTarget=false;
            worldHint=TextAt(worldCanvas,"마우스로 시나리오를 선택하세요",CaptionSize*2,Padding*WorldUnit,640,832,96,muted);worldHint.alignment=TextAnchor.UpperLeft;
        }

        // 눈앞 안내판은 거리가 가까우므로 1.6배 단위로 환산하여 조작판과 보이는 글자 크기를 맞춥니다.
        private void CreateTrayGuide()
        {
            worldTrayGuide=new GameObject("Repair Instructions",typeof(RectTransform),typeof(Canvas));worldTrayGuide.transform.SetParent(worldRepairTray.transform,false);
            var rect=worldTrayGuide.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(SidePanelWidth*TrayUnit,112*TrayUnit);rect.localScale=Vector3.one*.002f;rect.localPosition=new Vector3(0,.92f,0);
            worldTrayGuide.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            var card=WorldCard(rect,"Repair Guide Card",Vector2.zero,Vector2.one,panel);card.GetComponent<Image>().pixelsPerUnitMultiplier=1/TrayUnit;
            TextAt(rect,"부품 선택",Mathf.RoundToInt(HeadingSize*TrayUnit),Padding*TrayUnit,Padding*TrayUnit,(SidePanelWidth-2*Padding)*TrayUnit,40*TrayUnit,foreground);
            repairHint=TextAt(rect,"고장에 맞는 부품을 클릭하세요",Mathf.RoundToInt(BodySize*TrayUnit),Padding*TrayUnit,64*TrayUnit,(SidePanelWidth-2*Padding)*TrayUnit,32*TrayUnit,muted);
        }
        private void CreatePartPlate(Transform part,string title)
        {
            var obj=new GameObject("Part Nameplate",typeof(RectTransform),typeof(Canvas));obj.transform.SetParent(part,false);
            var rect=obj.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(128*TrayUnit,ControlHeight*TrayUnit);rect.localPosition=new Vector3(0,1.3f,0);var scale=part.lossyScale;rect.localScale=new Vector3(.002f/scale.x,.002f/scale.y,.002f/scale.z);
            obj.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            var card=WorldCard(rect,"Nameplate Card",Vector2.zero,Vector2.one,panel);card.GetComponent<Image>().pixelsPerUnitMultiplier=1/TrayUnit;
            var text=Label(rect,title,Mathf.RoundToInt(BodySize*TrayUnit),Vector2.zero,Vector2.one,foreground);text.rectTransform.offsetMin=new Vector2(Padding*TrayUnit,DenseGap*TrayUnit);text.rectTransform.offsetMax=-text.rectTransform.offsetMin;text.alignment=TextAnchor.MiddleCenter;
        }
        private Sprite CreateWorldCardSprite()
        {
            const int size=64;const float radius=12;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);texture.name="XR Console Rounded Card";texture.wrapMode=TextureWrapMode.Clamp;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float dx=Mathf.Max(radius-(x+.5f),0,(x+.5f)-(size-radius));float dy=Mathf.Max(radius-(y+.5f),0,(y+.5f)-(size-radius));
                float alpha=Mathf.Clamp01(radius-Mathf.Sqrt(dx*dx+dy*dy)+.5f);texture.SetPixel(x,y,new Color(1,1,1,alpha));
            }
            texture.Apply();return Sprite.Create(texture,new Rect(0,0,size,size),Vector2.one*.5f,100,0,SpriteMeshType.FullRect,new Vector4(radius,radius,radius,radius));
        }

        private RectTransform WorldCard(Transform parent,string name,Vector2 min,Vector2 max,Color color)
        {
            var rect=Box(parent,name,min,max,color);var image=rect.GetComponent<Image>();image.sprite=worldCardSprite;image.type=Image.Type.Sliced;image.pixelsPerUnitMultiplier=.5f;image.raycastTarget=false;return rect;
        }


        private void WorldButton(Transform parent,string objectName,string label,int row,Action action)
        {
            float height=ControlHeight*WorldUnit;float width=(SidePanelWidth-2*Padding)*WorldUnit;
            var rect=WorldCard(parent,objectName,Vector2.zero,Vector2.one,new Color(.21f,.31f,.34f));SetRow(rect,0,1,224+row*(ControlHeight+Gap)*WorldUnit,height,Padding*WorldUnit,Padding*WorldUnit);
            var fill=WorldCard(rect,"Button Fill",Vector2.zero,Vector2.one,new Color(.08f,.13f,.16f));fill.offsetMin=Vector2.one*4;fill.offsetMax=-Vector2.one*4;
            var collider=rect.gameObject.AddComponent<BoxCollider>();collider.size=new Vector3(width,height,8);collider.center=new Vector3(0,0,6);
            var interactable=rect.gameObject.AddComponent<XRSimpleInteractable>();var border=rect.GetComponent<Image>();
            interactable.hoverEntered.AddListener(_=>border.color=accent);interactable.hoverExited.AddListener(_=>border.color=new Color(.21f,.31f,.34f));interactable.selectEntered.AddListener(_=>action());
            TextAt(rect,(row+1).ToString("D2"),BodySize*2,Padding*WorldUnit,0,64,height,accent);
            TextAt(rect,label,BodySize*2,128,0,width-224,height,foreground);
            var arrow=TextAt(rect,"›",ValueSize*2,width-80,0,48,height,muted);arrow.alignment=TextAnchor.MiddleCenter;
        }
        private void RefreshWorldStation()
        {
            if(worldStage==null)return;
            worldStage.text=Core.TrainingActive?MiniTwinCore.StepNames[Core.Step]:"시나리오 선택";
            worldStepCounter.text=Core.TrainingActive?(Core.Step+1).ToString("D2")+" / 07":"";
            worldProgress.rectTransform.anchorMax=new Vector2(Core.TrainingActive?(Core.Step+1)/7f:Core.Result!=null?1:0,1);
            if(worldScenarios!=null)worldScenarios.SetActive(!Core.TrainingActive);
            bool mistake=Core.TrainingActive&&Core.Mistakes.Count>0&&Core.Mistakes[Core.Mistakes.Count-1].step==Core.Step;
            worldHint.text=mistake?Core.Feedback:Core.TrainingActive?(Core.Step==5?"눈앞의 부품 중 하나를 클릭하세요":"마우스로 버튼 선택 · 부품은 직접 선택 가능"):"마우스로 시나리오를 선택하세요";
            for(int i=0;i<7;i++)worldActionGroups[i].SetActive(Core.TrainingActive&&Core.Step==i);
            bool showTray=Core.TrainingActive&&Core.Step==5;
            if(showTray){repairHint.text=mistake?Core.Feedback:"고장에 맞는 부품을 클릭하세요";repairHint.color=mistake?new Color(1,.42f,.38f):muted;}
            if(showTray!=repairTrayVisible)
            {
                // 선택 단계에서는 카메라 앞에 고정해 이동하거나 고개를 돌려도 바로 고를 수 있게 합니다.
                if(showTray&&Camera.main!=null){worldRepairTray.transform.SetParent(Camera.main.transform,false);worldRepairTray.transform.localPosition=new Vector3(0,-.35f,1.8f);worldRepairTray.transform.localRotation=Quaternion.identity;worldRepairTray.transform.localScale=Vector3.one*.6f;}
                worldTraySurface.SetActive(showTray);worldTrayGuide.SetActive(showTray);
                for(int i=0;i<worldReplacementParts.Length;i++)
                {
                    var part=worldReplacementParts[i];
                    if(showTray){part.transform.SetParent(worldRepairTray.transform,true);part.transform.localPosition=new Vector3(-.55f+i*.55f,.2f,.23f);part.transform.localRotation=Quaternion.identity;}
                    part.SetActive(showTray);
                }
                repairTrayVisible=showTray;
            }
        }

        private TextMesh WorldLabel(Transform parent,string value,Vector3 localPosition)
        {
            var obj=new GameObject(value+" Label");obj.transform.SetParent(parent,false);obj.transform.localPosition=localPosition;
            // 버튼·부품의 크기가 글자 크기를 찌그러뜨리지 않도록 월드 배율을 보정합니다.
            Vector3 scale=parent.lossyScale;obj.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
            var label=obj.AddComponent<TextMesh>();label.font=font;label.text=value;label.fontSize=48;label.characterSize=.027f;label.anchor=TextAnchor.MiddleCenter;label.color=foreground;
            obj.GetComponent<MeshRenderer>().sharedMaterial=font.material;return label;
        }

        private RectTransform Box(Transform parent,string name,Vector2 min,Vector2 max,Color color)
        {
            var o=new GameObject(name,typeof(RectTransform),typeof(Image));o.transform.SetParent(parent,false);
            var rect=o.GetComponent<RectTransform>();SetRect(rect,min,max);o.GetComponent<Image>().color=color;return rect;
        }
        private Text Label(Transform parent,string value,int size,Vector2 min,Vector2 max,Color color)
        {
            var o=new GameObject("Label",typeof(RectTransform),typeof(Text));o.transform.SetParent(parent,false);SetRect(o.GetComponent<RectTransform>(),min,max);
            var text=o.GetComponent<Text>();text.font=font;text.text=value;text.fontSize=size;text.color=color;text.alignment=TextAnchor.MiddleLeft;
            text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;return text;
        }
        private Button Button(Transform parent,string value,Vector2 min,Vector2 max,Action action)
        {
            var rect=Box(parent,value,min,max,new Color(.14f,.22f,.26f));var button=rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(()=>action());var text=Label(rect,value,14,new Vector2(.04f,.04f),new Vector2(.96f,.96f),foreground);text.alignment=TextAnchor.MiddleCenter;return button;
        }
        private void SetRect(RectTransform rect,Vector2 min,Vector2 max)
        {rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        private void PositionCamera()
        {orbitCamera.transform.position=new Vector3(0,.9f,0)+Quaternion.Euler(pitch,yaw,0)*new Vector3(0,0,-distance);orbitCamera.transform.LookAt(new Vector3(0,.9f,0));}
        // [실제 코드에 남길 주석] 화면 HUD에서는 일반 마우스를, 3D 공간에서는 SDK point-and-click을 사용해 입력 소유권을 나눕니다.
        private void RouteVRPointer(Vector2 pointerPosition)
        {
            if(mode!="VR"||vrSimulator==null||vrInputModule==null||EventSystem.current==null)return;
            var pointer=new PointerEventData(EventSystem.current){position=pointerPosition};
            uiHits.Clear();EventSystem.current.RaycastAll(pointer,uiHits);
            bool overUI=uiHits.Count>0;
            vrSimulator.usePointAndClick=!overUI;
            if(overUI)vrInputModule.enableMouseInput=true;
        }
        private void Update()
        {
            if(Mouse.current!=null)RouteVRPointer(Mouse.current.position.ReadValue());
            TickHUD();
            if(orbitCamera==null||Mouse.current==null||hudDialog!=null||!orbitCamera.pixelRect.Contains(Mouse.current.position.ReadValue())||(EventSystem.current!=null&&EventSystem.current.IsPointerOverGameObject()))return;
            if(Mouse.current.rightButton.isPressed){Vector2 delta=Mouse.current.delta.ReadValue();yaw+=delta.x*.15f;pitch=Mathf.Clamp(pitch-delta.y*.15f,5,75);}
            distance=Mathf.Clamp(distance-Mouse.current.scroll.ReadValue().y*.003f,3,11);PositionCamera();
        }
        private void OnDestroy()
        {
            if(Core!=null)Core.Changed-=Refresh;
            if(worldCardSprite!=null){Destroy(worldCardSprite.texture);Destroy(worldCardSprite);}
            if(hudSprite!=null){Destroy(hudSprite.texture);Destroy(hudSprite);}
            if(modeManager!=null && originalLoaders!=null)modeManager.TrySetLoaders(originalLoaders);
        }
    }
}
