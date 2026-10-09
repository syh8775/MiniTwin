using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace MiniTwin
{
    // [실제 코드에 남길 주석] 화면 표현만 분리하며 센서·훈련 판정은 기존 Core를 사용합니다.
    public sealed partial class MiniTwinApp
    {
        private readonly Color uiPaper=new Color(.07f,.105f,.13f,.96f);
        private readonly Color uiSurface=new Color(.065f,.095f,.12f,.94f);
        private readonly Color uiInk=new Color(.90f,.94f,.96f);
        private readonly Color uiSub=new Color(.58f,.68f,.73f);
        private readonly Color uiLine=new Color(.20f,.29f,.34f);
        private readonly Color uiBlue=new Color(.10f,.43f,.51f);
        private readonly Color uiGood=new Color(.25f,.84f,.64f);
        private readonly Color uiAmber=new Color(1f,.70f,.29f);
        private readonly Color uiRed=new Color(1f,.37f,.40f);
        private readonly Color uiNavy=new Color(.055f,.10f,.16f);
        private readonly Rect viewerRect=new Rect(0,0,1,1);
        // 화면 글자와 간격은 역할별 공통 값으로 관리합니다. 3D UI는 투영 크기에 맞춰 별도 환산합니다.
        private const int CaptionSize=16, BodySize=18, HeadingSize=24, ValueSize=28;
        private const float Padding=16, Gap=16, DenseGap=8, ScreenMargin=24, ControlHeight=48;
        private const float HeaderHeight=96, PanelHeaderHeight=80, SidePanelWidth=448;
        private const float SidePanelTop=HeaderHeight+ScreenMargin, SensorCardHeight=120;
        private const float WorkPanelWidth=1104, WorkPanelHeight=264, ChoiceHeight=80;
        private const float DialogWidth=1120, DialogHeight=640, WorldUnit=2, TrayUnit=1.6f;
        private Text sensorFeedback,resultScore;
        private Sprite hudSprite;
        private Text runningText,refreshText,clockText,selectionText,deviceText;
        private readonly Text[] sensorStates=new Text[4];
        private readonly Image[] sensorStateSurfaces=new Image[4];
        private readonly Image[] stepSurfaces=new Image[7];
        private readonly Button[] deviceButtons=new Button[3];
        private Button pauseButton,recordButton,errorsButton;
        private RectTransform hudDialog;
        private string lastClock;
        private RectTransform sensorDrawer,summaryDrawer;
        private TrainingResult shownResult;
        private string shownPart;
        private Button resultToggle;
        private RectTransform trainingNotice;

        private RectTransform Surface(Transform parent,string name,Vector2 min,Vector2 max,Color fill,bool border=true)
        {
            if(hudSprite==null)hudSprite=CreateWorldCardSprite();
            var rect=Box(parent,name,min,max,fill);var image=rect.GetComponent<Image>();image.sprite=hudSprite;image.type=Image.Type.Sliced;
            if(border){var outline=rect.gameObject.AddComponent<Outline>();outline.effectColor=uiLine;outline.effectDistance=new Vector2(1,-1);}
            return rect;
        }
        private Text Copy(Transform parent,string value,int size,Vector2 min,Vector2 max,Color color,bool bold=false)
        {var text=Label(parent,value,size,min,max,color);text.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;return text;}
        private Button UiButton(Transform parent,string label,Vector2 min,Vector2 max,Action action,bool primary=false)
        {
            var rect=Surface(parent,label,min,max,primary?uiBlue:new Color(.13f,.20f,.24f),!primary);var button=rect.gameObject.AddComponent<Button>();button.onClick.AddListener(()=>action());
            var colors=button.colors;colors.highlightedColor=new Color(.88f,.94f,1);colors.pressedColor=new Color(.72f,.84f,.95f);colors.disabledColor=new Color(.45f,.50f,.54f);button.colors=colors;
            var text=Copy(rect,label,BodySize,Vector2.zero,Vector2.one,primary?Color.white:uiInk,true);text.rectTransform.offsetMin=new Vector2(Padding,DenseGap);text.rectTransform.offsetMax=new Vector2(-Padding,-DenseGap);text.alignment=TextAnchor.MiddleCenter;
            return button;
        }
        private void BuildUI()
        {
            var obj=new GameObject("MiniTwin HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            obj.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=obj.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            canvasRoot=obj.GetComponent<RectTransform>();
            // 설비 조작이 주 작업이므로 카메라는 전체 화면, 정보는 접을 수 있는 보조 패널입니다.
            BuildSceneHeader();BuildSensors();
            if(mode=="VR")BuildVRTrainingUI();else BuildInspectionUI();
        }
        // 처음 닫힌 센서 패널의 한글까지 등록한 뒤, 재배치된 글자 메시를 갱신합니다.
        private void WarmText()
        {
            var labels=new System.Collections.Generic.List<Text>(canvasRoot.GetComponentsInChildren<Text>(true));
            if(worldCanvas!=null)labels.AddRange(worldCanvas.GetComponentsInChildren<Text>(true));
            if(worldRepairTray!=null)labels.AddRange(worldRepairTray.GetComponentsInChildren<Text>(true));
            foreach(var label in labels)if(label.font!=null)label.font.RequestCharactersInTexture(label.text,label.fontSize,label.fontStyle);
            foreach(var label in labels)label.SetAllDirty();
            Canvas.ForceUpdateCanvases();
        }
        private void SetRow(RectTransform rect,float left,float right,float top,float height,float insetLeft=Padding,float insetRight=Padding)
        {rect.anchorMin=new Vector2(left,1);rect.anchorMax=new Vector2(right,1);rect.offsetMin=new Vector2(insetLeft,-top-height);rect.offsetMax=new Vector2(-insetRight,-top);}
        private Text RowText(Transform parent,string value,int size,float top,float height,Color color,bool bold=false)
        {var text=Copy(parent,value,size,Vector2.zero,Vector2.one,color,bold);SetRow(text.rectTransform,0,1,top,height);return text;}

        // UI_LAYOUT.md의 공통 치수를 적용합니다. 같은 역할의 버튼 높이는 화면마다 달라지지 않습니다.
        private void Pin(RectTransform rect,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size)
        {rect.anchorMin=rect.anchorMax=anchor;rect.pivot=pivot;rect.anchoredPosition=position;rect.sizeDelta=size;}
        private void Place(RectTransform rect,float left,float top,float width,float height)
        {Pin(rect,new Vector2(0,1),new Vector2(0,1),new Vector2(left,-top),new Vector2(width,height));}
        private RectTransform Frame(Transform parent,string name)
        {var obj=new GameObject(name,typeof(RectTransform));obj.transform.SetParent(parent,false);return obj.GetComponent<RectTransform>();}
        private Text TextAt(Transform parent,string value,int size,float left,float top,float width,float height,Color color,bool bold=false)
        {var text=Copy(parent,value,size,Vector2.zero,Vector2.one,color,bold);Place(text.rectTransform,left,top,width,height);return text;}
        private Button ButtonAt(Transform parent,string name,float left,float top,float width,Action action,bool primary=false)
        {var button=UiButton(parent,name,Vector2.zero,Vector2.one,action,primary);Place(button.GetComponent<RectTransform>(),left,top,width,ControlHeight);return button;}
        private RectTransform ButtonGroup(Transform parent,string name,float left,float top,float width,float height)
        {
            var group=Frame(parent,name);Place(group,left,top,width,height);
            var layout=group.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=Gap;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=true;
            return group;
        }
        private Button GroupButton(Transform parent,string name,Action action,bool primary=false)
        {
            var button=UiButton(parent,name,Vector2.zero,Vector2.one,action,primary);
            var element=button.gameObject.AddComponent<LayoutElement>();element.minWidth=0;element.preferredWidth=0;element.flexibleWidth=1;return button;
        }
        private RectTransform SidePanel(string name,float height)
        {var panelRect=Surface(canvasRoot,name,Vector2.zero,Vector2.one,uiSurface);Pin(panelRect,Vector2.one,Vector2.one,new Vector2(-ScreenMargin,-SidePanelTop),new Vector2(SidePanelWidth,height));return panelRect;}
        private void PanelTitle(RectTransform panelRect,string title,string closeName,Action close)
        {
            var label=RowText(panelRect,title,HeadingSize,Padding,ControlHeight,uiInk,true);SetRow(label.rectTransform,0,1,Padding,ControlHeight,Padding,Padding+80+Gap);
            var button=UiButton(panelRect,closeName,Vector2.zero,Vector2.one,close);button.GetComponentInChildren<Text>().text="닫기";
            Pin(button.GetComponent<RectTransform>(),Vector2.one,Vector2.one,new Vector2(-Padding,-Padding),new Vector2(80,ControlHeight));
        }
        private void BuildSceneHeader()
        {
            var header=Box(canvasRoot,"Scene Header",Vector2.zero,Vector2.one,new Color(.035f,.057f,.074f,.97f));SetRow(header,0,1,0,HeaderHeight,0,0);
            TextAt(header,"MINITWIN",HeadingSize,ScreenMargin,Padding,240,40,foreground,true);
            TextAt(header,"설비 점검 · 시뮬레이션",CaptionSize,ScreenMargin,56,240,24,uiSub);
            var tabs=ButtonGroup(header,"Mode Tabs",296,ScreenMargin,512,ControlHeight);
            string[] names={"트윈 대시보드","AR 점검","VR 훈련"};string[] modes={"Dashboard","AR","VR"};
            for(int i=0;i<3;i++){string target=modes[i];var button=GroupButton(tabs,names[i],()=>SwitchMode(target),mode==target);if(!Application.isEditor&&target!="Dashboard")button.interactable=false;}
            string[] toolNames={"센서 정보","시점 복원","초기화","조작 안내"};Action[] actions={()=>ToggleDrawer(sensorDrawer),RestoreView,Core.ResetSimulation,ShowHelp};
            for(int i=0;i<4;i++){var button=UiButton(header,toolNames[i],Vector2.zero,Vector2.one,actions[i]);Pin(button.GetComponent<RectTransform>(),Vector2.one,Vector2.one,new Vector2(-ScreenMargin-(3-i)*(128+Gap),-ScreenMargin),new Vector2(128,ControlHeight));}
            if(mode=="VR"){resultToggle=UiButton(header,"결과",Vector2.zero,Vector2.one,()=>ToggleDrawer(summaryDrawer));Pin(resultToggle.GetComponent<RectTransform>(),Vector2.one,Vector2.one,new Vector2(-ScreenMargin-4*(128+Gap),-ScreenMargin),new Vector2(128,ControlHeight));}
            var state=Surface(canvasRoot,"Metric 0",Vector2.zero,Vector2.one,uiSurface);Place(state,ScreenMargin,SidePanelTop,SidePanelWidth,64);
            TextAt(state,"PUMP-01",BodySize,Padding,Padding,240,32,uiSub,true);
            status=TextAt(state,"",BodySize,272,Padding,160,32,uiGood,true);status.alignment=TextAnchor.MiddleRight;state.gameObject.AddComponent<Button>().onClick.AddListener(ShowAlarmDialog);
            if(mode=="VR")
            {
                var devices=Surface(canvasRoot,"XR Device Controls",Vector2.zero,Vector2.one,uiSurface);Place(devices,ScreenMargin,SidePanelTop+64+Gap,SidePanelWidth,152);
                RowText(devices,"키보드·마우스 조작 대상",BodySize,Padding,32,uiInk,true);
                var targets=ButtonGroup(devices,"Device Buttons",Padding,56,SidePanelWidth-2*Padding,ControlHeight);
                string[] labels={"카메라 이동","왼손 이동","오른손 이동"};for(int i=0;i<3;i++){int index=i;deviceButtons[i]=GroupButton(targets,labels[i],()=>SetDevice(index));}
                deviceText=RowText(devices,"",CaptionSize,112,24,uiSub);
            }
            else if(mode=="AR")
            {
                var tracker=Surface(canvasRoot,"Tracking Status",Vector2.zero,Vector2.one,uiSurface);Place(tracker,ScreenMargin,SidePanelTop+64+Gap,SidePanelWidth,64);
                tracking=RowText(tracker,"",BodySize,Padding,32,uiInk,true);
            }
            var guide=Surface(canvasRoot,"Interaction Guide",Vector2.zero,Vector2.one,uiSurface);Pin(guide,Vector2.zero,Vector2.zero,new Vector2(ScreenMargin,ScreenMargin),new Vector2(800,ControlHeight));
            RowText(guide,mode=="VR"?"WASD 이동 · 마우스 선택 · 우클릭 회전":mode=="AR"?"우클릭 + WASD 이동 · 가상 마커 추적":"우클릭 회전 · 휠 확대 · 부품 선택",BodySize,DenseGap,32,uiSub);
        }
        private void ToggleDrawer(RectTransform drawer)
        {if(drawer==null)return;bool show=!drawer.gameObject.activeSelf;if(show){if(drawer==sensorDrawer&&summaryDrawer!=null)summaryDrawer.gameObject.SetActive(false);if(drawer==summaryDrawer)sensorDrawer.gameObject.SetActive(false);}drawer.gameObject.SetActive(show);}
        private void BuildSensors()
        {
            sensorDrawer=SidePanel("Sensor Drawer",896);PanelTitle(sensorDrawer,"센서 정보","센서 닫기",()=>sensorDrawer.gameObject.SetActive(false));
            string[] names={"모터 온도","진동","토출 압력","유량"};string[] ranges={"주의 ≥ "+thresholds.tempWarning+" °C","주의 ≥ "+thresholds.vibrationWarning+" mm/s","주의 < "+thresholds.pressureNormal+" bar","주의 < "+thresholds.flowNormal+" m³/h"};string[] ids={"Motor","Bearing","Pipe","Pipe"};
            for(int i=0;i<4;i++)
            {
                string part=ids[i];var card=Surface(sensorDrawer,"Sensor "+i,Vector2.zero,Vector2.one,uiPaper);Place(card,Padding,PanelHeaderHeight+i*(SensorCardHeight+DenseGap),SidePanelWidth-2*Padding,SensorCardHeight);
                card.gameObject.AddComponent<Button>().onClick.AddListener(()=>Core.Inspect(part));
                TextAt(card,names[i],CaptionSize,Padding,Padding,240,24,uiSub,true);
                var badge=Surface(card,"Sensor State",Vector2.zero,Vector2.one,uiPaper,false);Place(badge,320,Padding,80,24);sensorStateSurfaces[i]=badge.GetComponent<Image>();sensorStates[i]=Copy(badge,"",CaptionSize,Vector2.zero,Vector2.one,uiGood,true);sensorStates[i].alignment=TextAnchor.MiddleCenter;
                sensorTexts[i]=TextAt(card,"",ValueSize,Padding,40,208,48,uiInk,true);
                TextAt(card,ranges[i],CaptionSize,232,48,168,32,uiSub).alignment=TextAnchor.MiddleRight;
                var plot=Frame(card,"60 Second Graph");Place(plot,Padding,88,384,16);graphs[i]=plot.gameObject.AddComponent<SensorGraph>();graphs[i].core=Core;graphs[i].sensor=i;graphs[i].color=uiBlue;graphs[i].raycastTarget=false;
            }
            pauseButton=ButtonAt(sensorDrawer,"센서 갱신 정지/재개",Padding,600,SidePanelWidth-2*Padding,Core.ToggleFreeze);
            runningText=TextAt(sensorDrawer,"",CaptionSize,Padding,656,192,24,uiSub);refreshText=TextAt(sensorDrawer,"",CaptionSize,240,656,192,24,uiSub);refreshText.alignment=TextAnchor.MiddleRight;
            var details=Surface(sensorDrawer,"Inspection Details",Vector2.zero,Vector2.one,uiPaper);Place(details,Padding,696,416,184);
            selectionText=RowText(details,"",BodySize,Padding,64,uiInk,true);selectionText.alignment=TextAnchor.UpperLeft;
            sensorFeedback=RowText(details,"",CaptionSize,96,72,uiSub);sensorFeedback.alignment=TextAnchor.UpperLeft;feedback=sensorFeedback;
            sensorDrawer.gameObject.SetActive(false);
        }
        private void BuildInspectionUI()
        {
            var panelRect=Surface(canvasRoot,"Inspection Workspace",Vector2.zero,Vector2.one,uiSurface);Pin(panelRect,Vector2.zero,Vector2.zero,new Vector2(ScreenMargin,ScreenMargin+ControlHeight+Gap),new Vector2(WorkPanelWidth,WorkPanelHeight));
            var title=RowText(panelRect,"점검 조작",HeadingSize,Padding,40,uiInk,true);SetRow(title.rectTransform,0,1,Padding,40,Padding,160);
            var content=Frame(panelRect,"Inspection Content");Place(content,0,64,WorkPanelWidth,184);
            RowText(content,"고장 조건을 선택하고 센서와 설비 변화를 확인하세요.",CaptionSize,0,24,uiSub);
            Button fold=null;fold=ButtonAt(panelRect,"화면 조작판",WorkPanelWidth-Padding-128,Padding,128,()=>{bool expanded=!content.gameObject.activeSelf;content.gameObject.SetActive(expanded);panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,expanded?WorkPanelHeight:PanelHeaderHeight);fold.GetComponentInChildren<Text>().text=expanded?"조작판 접기":"조작판 열기";});fold.GetComponentInChildren<Text>().text="조작판 접기";
            TextAt(content,"고장 조건",BodySize,Padding,64,112,32,uiSub,true);BuildScenarios(content,0,1);
            TextAt(content,"부품 점검",BodySize,Padding,144,112,32,uiSub,true);
            var parts=ButtonGroup(content,"Inspection Buttons",144,136,944,ControlHeight);string[] ids={"Motor","Bearing","Pipe"};
            for(int i=0;i<3;i++){string part=ids[i];GroupButton(parts,InspectionLabel(i),()=>Core.Inspect(part));}
        }
        private void BuildScenarios(RectTransform parent,float bottom,float top)
        {
            var group=ButtonGroup(parent,"Scenario Buttons",144,40,944,ChoiceHeight);string[] notes={"온도 상승 확인","진동 이상 확인","압력·유량 저하 확인"};
            for(int i=0;i<3;i++)
            {
                FaultType fault=(FaultType)(i+1);var button=GroupButton(group,MiniTwinCore.FaultName(fault),()=>Core.SetFault(fault));
                var title=button.GetComponentInChildren<Text>();title.alignment=TextAnchor.MiddleLeft;SetRow(title.rectTransform,0,1,DenseGap,32);
                RowText(button.transform,notes[i],CaptionSize,48,24,uiSub);scenarioButtons[i]=button;
            }
        }
        private void BuildVRTrainingUI()
        {
            summaryDrawer=SidePanel("Training Summary",528);PanelTitle(summaryDrawer,"수행 결과","결과 닫기",()=>summaryDrawer.gameObject.SetActive(false));
            resultScore=RowText(summaryDrawer,"",ValueSize,96,48,uiInk,true);
            result=RowText(summaryDrawer,"",BodySize,160,112,uiInk);result.alignment=TextAnchor.UpperLeft;
            feedback=RowText(summaryDrawer,"",CaptionSize,288,88,uiSub);feedback.alignment=TextAnchor.UpperLeft;
            recordButton=ButtonAt(summaryDrawer,"기록 폴더 열기",Padding,400,416,OpenRecords);
            errorsButton=ButtonAt(summaryDrawer,"오류 기록 보기",Padding,464,416,ShowErrors);summaryDrawer.gameObject.SetActive(false);
        }
        private static string InspectionLabel(int index)
        {return index==0?"모터·온도 점검":index==1?"베어링·진동 점검":"배관·압력·유량 점검";}
        private void RefreshTrainingUI()
        {
            foreach(var button in scenarioButtons)if(button!=null)button.interactable=!Core.TrainingActive;
            if(mode!="VR")return;
            resultToggle.interactable=Core.Result!=null;
            if(Core.Result!=null&&Core.Result!=shownResult){shownResult=Core.Result;sensorDrawer.gameObject.SetActive(false);summaryDrawer.gameObject.SetActive(true);}
            if(Core.Result==null){shownResult=null;summaryDrawer.gameObject.SetActive(false);}
            recordButton.interactable=Core.Result!=null&&Core.LastSavedPath!=null;errorsButton.interactable=Core.Result!=null&&Core.Result.mistakes.Count>0;
            errorsButton.GetComponentInChildren<Text>().text="오류 기록 "+(Core.Result==null?0:Core.Result.mistakes.Count)+"건";
        }
        public void Refresh()
        {
            if(Core.Current==null||status==null)return;
            status.text=StateLabel(Core.State);status.color=StateColor(Core.State);
            runningText.text=Core.Running?"운전 중":"정지";refreshText.text=Core.FreezeSensors?"일시 정지":"1초 간격";
            pauseButton.GetComponentInChildren<Text>().text=Core.FreezeSensors?"센서 갱신 재개":"센서 갱신 일시정지";
            var values=Core.Current.sensors;float[] numbers={values.motorTemp,values.vibration,values.pressure,values.flow};string[] units={"°C","mm/s","bar","m³/h"};
            for(int i=0;i<4;i++){var state=SensorState(i,numbers[i]);Color tone=StateColor(state);sensorTexts[i].text=numbers[i].ToString(i==0||i==3?"F1":"F2")+" "+units[i];sensorTexts[i].color=state==PumpState.DANGER?uiRed:uiInk;sensorStates[i].text=StateLabel(state);sensorStates[i].color=tone;sensorStateSurfaces[i].color=Color.Lerp(uiSurface,tone,.18f);graphs[i].color=tone;graphs[i].SetVerticesDirty();}
            feedback.text=UiFeedback(Core.Feedback);if(sensorFeedback!=feedback)sensorFeedback.text=feedback.text;RefreshTrainingUI();RefreshWorldStation();
            if(selectionText!=null){selectionText.text=PartSummary();if(mode!="VR"&&Core.SelectedPart!=null&&Core.SelectedPart!=shownPart)sensorDrawer.gameObject.SetActive(true);shownPart=Core.SelectedPart;}
            if(resultScore!=null)resultScore.text=Core.Result==null?"":Core.Result.score+"점  ·  "+Core.Result.grade+"등급";
            if(result!=null)result.text=Core.Result==null?"절차에 따라 조작하세요.\n부품을 점검하고 원인을 선택하면\n안전 조치 단계가 이어집니다.":"정확도 "+Core.Result.accuracy+"/50   안전 "+Core.Result.safety+"/30\n시간 "+Core.Result.timeScore+"/20   소요 "+Core.Result.duration.ToString("F0")+"초\n"+(Core.LastSavedPath==null?"기록 저장 실패":"훈련 기록 저장 완료");
        }
        private string PartSummary()
        {
            var s=Core.Current.sensors;string part=Core.SelectedPart;
            if(part=="Motor"||part=="Fan")return MiniTwinCore.PartLabel(part)+" · 온도 점검\n현재 "+s.motorTemp.ToString("F1")+" °C";
            if(part=="Bearing")return "베어링 · 진동 점검\n현재 "+s.vibration.ToString("F2")+" mm/s";
            if(part=="Pipe")return "흡입 배관 · 압력·유량 점검\n"+s.pressure.ToString("F2")+" bar  /  "+s.flow.ToString("F1")+" m³/h";
            if(part=="Impeller")return "임펠러 · 유량 점검\n현재 "+s.flow.ToString("F1")+" m³/h";
            return "모델 또는 센서 카드를 선택하세요.\n선택한 부품의 센서 정보가 표시됩니다.";
        }
        private PumpState SensorState(int index,float value)
        {
            if(Core.State==PumpState.UNKNOWN)return PumpState.UNKNOWN;if(!Core.Running)return PumpState.STOPPED;
            if(index==0)return value>thresholds.tempDanger?PumpState.DANGER:value>=thresholds.tempWarning?PumpState.WARNING:PumpState.NORMAL;
            if(index==1)return value>thresholds.vibrationDanger?PumpState.DANGER:value>=thresholds.vibrationWarning?PumpState.WARNING:PumpState.NORMAL;
            if(index==2)return value<thresholds.pressureDanger?PumpState.DANGER:value<thresholds.pressureNormal?PumpState.WARNING:PumpState.NORMAL;
            return value<thresholds.flowDanger?PumpState.DANGER:value<thresholds.flowNormal?PumpState.WARNING:PumpState.NORMAL;
        }
        private string StateLabel(PumpState state)
        {return state==PumpState.NORMAL?"정상":state==PumpState.WARNING?"주의":state==PumpState.DANGER?"위험":state==PumpState.STOPPED?"운전 정지":"미확인";}
        private Color StateColor(PumpState state)
        {return state==PumpState.NORMAL?uiGood:state==PumpState.WARNING?uiAmber:state==PumpState.DANGER?uiRed:uiSub;}
        private string UiFeedback(string text)
        {return text=="동일 seed로 초기화했습니다."?"설비·센서·훈련 상태를 초기화했습니다.":text;}
        private void TickHUD()
        {
            string time=DateTime.Now.ToString("yyyy.MM.dd  HH:mm:ss");if(time!=lastClock&&clockText!=null){clockText.text=time;lastClock=time;}
            if(training!=null&&Core.TrainingActive)training.text="현재 단계 "+(Core.Step+1)+"/7 · "+MiniTwinCore.StepNames[Core.Step]+" · "+Core.Duration.ToString("F0")+"초";
            if(tracking!=null)tracking.text=TrackingStatus;
            if(deviceText!=null&&vrSimulator!=null){var state=vrSimulator.currentState;deviceText.text=state.manipulatingHMD?"선택: 카메라":state.manipulatingLeftDevice?"선택: 왼손 컨트롤러":"선택: 오른손 컨트롤러";for(int i=0;i<3;i++){bool active=i==0?state.manipulatingHMD:i==1?state.manipulatingLeftDevice:state.manipulatingRightDevice;deviceButtons[i].GetComponent<Image>().color=active?new Color(.12f,.39f,.46f):new Color(.13f,.20f,.24f);}}
        }
        private void SetDevice(int index)
        {
            if(vrSimulator==null)return;
            // XRI 3.6의 currentState는 읽기 전용입니다. SDK의 공개 buffered setter를 사용합니다.
            #pragma warning disable CS0618
            vrSimulator.targetedDeviceInput=index==0?TargetedDevices.HMD:index==1?TargetedDevices.LeftDevice:TargetedDevices.RightDevice;
            #pragma warning restore CS0618
        }
        private void ConfigureView()
        {
            if(Camera.main!=null){Camera.main.rect=viewerRect;if(mode=="VR")Camera.main.fieldOfView=60;if(mode!="AR"){Camera.main.clearFlags=CameraClearFlags.SolidColor;Camera.main.backgroundColor=bg;}}
            if(mode=="VR")
            {
                SetDevice(0);
                // 초기 시점에서는 공간 조작판도 HUD와 같은 448 너비와 왼쪽 정렬선을 갖습니다.
                // 배치 후에는 월드에 남아 사용자가 카메라로 둘러볼 수 있습니다.
                if(worldCanvas!=null&&Camera.main!=null)
                {
                    Canvas.ForceUpdateCanvases();var camera=Camera.main;var size=canvasRoot.rect.size;float depth=4.2f;
                    float top=SidePanelTop+64+Gap+152+ScreenMargin;float x=(ScreenMargin+SidePanelWidth*.5f)/size.x;float y=1-(top+worldCanvas.sizeDelta.y/WorldUnit*.5f)/size.y;
                    var center=camera.ViewportToWorldPoint(new Vector3(x,y,depth));var right=camera.ViewportToWorldPoint(new Vector3(x+SidePanelWidth/size.x,y,depth));
                    worldCanvas.parent.position=center+camera.transform.forward*.07f;worldCanvas.parent.rotation=camera.transform.rotation;
                    worldCanvas.localScale=Vector3.one*Vector3.Distance(center,right)/worldCanvas.sizeDelta.x;
                }
            }
            if(mode=="VR")foreach(var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(canvas.name.StartsWith("XR Interaction Simulator UI"))canvas.gameObject.SetActive(false);
        }
        private void RestoreView()
        {
            if(mode=="Dashboard"){yaw=38;pitch=22;distance=6.5f;PositionCamera();}
            else if(mode=="VR"){var origin=FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();origin.MatchOriginUpCameraForward(Vector3.up,Vector3.forward);origin.MoveCameraToWorldLocation(new Vector3(0,1.6f,-1));}
            #if UNITY_EDITOR
            else{var pose=FindAnyObjectByType<UnityEngine.XR.Simulation.SimulationCameraPoseProvider>();if(pose!=null){pose.transform.position=new Vector3(0,2,-4);pose.transform.rotation=Quaternion.Euler(15,0,0);}}
            #endif
        }

        private RectTransform Dialog(string title)
        {
            if(hudDialog!=null)Destroy(hudDialog.gameObject);
            hudDialog=Box(canvasRoot,"Dialog Backdrop",Vector2.zero,Vector2.one,new Color(.02f,.06f,.10f,.55f));
            var dialog=Surface(hudDialog,"Dialog",Vector2.zero,Vector2.one,uiSurface);Pin(dialog,Vector2.one*.5f,Vector2.one*.5f,Vector2.zero,new Vector2(DialogWidth,DialogHeight));
            PanelTitle(dialog,title,"닫기",()=>{Destroy(hudDialog.gameObject);hudDialog=null;});return dialog;
        }
        private void ShowHelp()
        {
            var dialog=Dialog("조작 안내");
            string help=mode=="Dashboard"?"설비 대시보드\n\n모델의 부품 또는 센서 카드를 클릭해 점검 정보를 확인합니다.\n우클릭 드래그로 모델을 회전하고 마우스 휠로 확대합니다.\n시나리오를 선택하면 해당 고장 조건의 센서 변화를 확인할 수 있습니다.\n센서 갱신을 정지하면 데이터 미확인 상태를 시험할 수 있습니다.":mode=="AR"?"AR 현장 점검\n\n우클릭을 누른 채 WASD로 가상 카메라를 이동합니다.\n마커를 감지하면 펌프가 표시됩니다.\n추적이 끊기면 마지막 확인 위치를 유지하며 안내가 표시됩니다.\n‘시점 복원’을 누르면 마커를 바라보는 초기 위치로 돌아갑니다.":"VR 정비 훈련\n\n3D 조작판에서 시나리오를 고르고 같은 판에서 훈련을 진행합니다.\n‘카메라 이동’은 둘러보기, ‘왼손 이동’·‘오른손 이동’은 가상 손 위치 조절입니다.\nWASD로 선택한 대상을 이동하고, 3D 조작판의 버튼을 마우스로 클릭하세요.\n현재 단계에 해당하는 버튼만 표시됩니다.\n부품 조치 단계에서는 눈앞의 올바른 부품이나 이름표를 클릭하면 됩니다.\n완료 후 점수·오류 기록·저장된 결과를 확인합니다.";
            // 조작 안내는 긴 문장도 읽기 쉽도록 일반 본문보다 크게 표시합니다.
            var helpText=TextAt(dialog,help,HeadingSize,Padding,PanelHeaderHeight+Padding,DialogWidth-2*Padding,DialogHeight-PanelHeaderHeight-2*Padding,uiInk);
            helpText.alignment=TextAnchor.UpperLeft;
            helpText.lineSpacing=1.15f;
        }

        private void ShowAlarmDialog()
        {
            var dialog=Dialog("설비 상태 상세");var s=Core.Current.sensors;
            TextAt(dialog,"PUMP-01  ·  "+StateLabel(Core.State),ValueSize,Padding,96,DialogWidth-2*Padding,48,StateColor(Core.State),true);
            string[] names={"모터 온도","진동","토출 압력","유량"};string[] values={s.motorTemp.ToString("F1")+" °C",s.vibration.ToString("F2")+" mm/s",s.pressure.ToString("F2")+" bar",s.flow.ToString("F1")+" m³/h"};
            for(int i=0;i<4;i++){TextAt(dialog,names[i],BodySize,Padding,176+i*64,640,48,uiSub);TextAt(dialog,values[i],ValueSize,720,176+i*64,DialogWidth-720-Padding,48,uiInk).alignment=TextAnchor.MiddleRight;}
            var note=TextAt(dialog,"수집 시각  "+DateTimeOffset.Parse(Core.Current.timestamp).ToString("yyyy.MM.dd HH:mm:ss")+"\n가상 센서 기반 시뮬레이션 · 경계값은 데모 설정",CaptionSize,Padding,464,DialogWidth-2*Padding,80,uiSub);note.alignment=TextAnchor.UpperLeft;
        }
        private void OpenRecords()
        {if(Core.LastSavedPath!=null)Application.OpenURL(new Uri(System.IO.Path.GetDirectoryName(Core.LastSavedPath)).AbsoluteUri);}
        private void ShowErrors()
        {
            if(Core.Result==null||Core.Result.mistakes.Count==0)return;
            var dialog=Dialog("훈련 오류 기록  ·  "+Core.Result.mistakes.Count+"건");
            var scrollObject=new GameObject("Error Scroll",typeof(RectTransform),typeof(ScrollRect));scrollObject.transform.SetParent(dialog,false);var scrollRect=scrollObject.GetComponent<RectTransform>();Place(scrollRect,Padding,PanelHeaderHeight+Padding,DialogWidth-2*Padding,DialogHeight-PanelHeaderHeight-2*Padding);
            var viewport=Box(scrollRect,"Viewport",Vector2.zero,Vector2.one,uiSurface);viewport.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            var contentObject=new GameObject("Content",typeof(RectTransform),typeof(VerticalLayoutGroup),typeof(ContentSizeFitter));contentObject.transform.SetParent(viewport,false);var content=contentObject.GetComponent<RectTransform>();content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.offsetMin=content.offsetMax=Vector2.zero;
            var layout=contentObject.GetComponent<VerticalLayoutGroup>();layout.spacing=Gap;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandHeight=false;contentObject.GetComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            foreach(var item in Core.Result.mistakes){var row=Surface(content,"Error Record",Vector2.zero,Vector2.one,uiPaper,false);row.gameObject.AddComponent<LayoutElement>().preferredHeight=160;RowText(row,item.seconds.ToString("F0")+"초  ·  "+item.expected,BodySize,Padding,32,uiInk,true);var body=RowText(row,"수행: "+item.action+"\n"+item.detail,BodySize,64,80,uiSub);body.alignment=TextAnchor.UpperLeft;}
            var scroll=scrollObject.GetComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;
            Canvas.ForceUpdateCanvases();scroll.verticalNormalizedPosition=1;
        }
    }
}
