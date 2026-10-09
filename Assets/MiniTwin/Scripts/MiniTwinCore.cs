using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiniTwin
{
    public enum PumpState { NORMAL, WARNING, DANGER, STOPPED, UNKNOWN }
    public enum FaultType { None, Overheat, Vibration, LowFlow }

    [Serializable]
    public sealed class SensorValues
    {
        public float motorTemp, vibration, pressure, flow;
        public SensorValues(float temp, float vib, float press, float volume)
        { motorTemp = temp; vibration = vib; pressure = press; flow = volume; }
    }

    [Serializable]
    public sealed class PumpSnapshot
    {
        public int snapshotId;
        public string timestamp, equipmentId = "PUMP-01", source = "SIMULATED", state, scenario;
        public bool isRunning;
        public SensorValues sensors;
    }

    [Serializable]
    public sealed class TrainingMistake
    {
        public float seconds;
        public int step, snapshotId;
        public string action, expected, detail;
        public SensorValues sensors;
    }

    [Serializable]
    public sealed class TrainingResult
    {
        public string traineeId = "LOCAL", scenarioId, grade, completedAt;
        public int score, accuracy, safety, timeScore;
        public float duration;
        public List<TrainingMistake> mistakes = new List<TrainingMistake>();
    }

    public sealed class MiniTwinCore : MonoBehaviour
    {
        public PumpThresholds thresholds;
        public int seed = 8775;
        public PumpSnapshot Current { get; private set; }
        public PumpState State { get; private set; }
        public FaultType Fault { get; private set; }
        public bool Running { get; private set; } = true;
        public bool FreezeSensors { get; private set; }
        public bool TrainingActive { get; private set; }
        public int Step { get; private set; }
        public float Duration { get; private set; }
        public TrainingResult Result { get; private set; }
        public string Feedback { get; private set; } = "정상 운전 중입니다. 고장 시나리오를 선택해 보세요.";
        public string LastSavedPath { get; private set; }
        public string SelectedPart { get; private set; }
        public List<PumpSnapshot> History { get; private set; } = new List<PumpSnapshot>();
        public List<TrainingMistake> Mistakes { get; private set; } = new List<TrainingMistake>();
        public event Action Changed;
        private System.Random random;
        private float accumulator, faultAge, dataAge;
        private int sequence;
        private bool firstCauseCorrect = true, orderCorrect = true, causeAttempted;
        private readonly HashSet<string> safetyMisses = new HashSet<string>();
        public static readonly string[] StepNames = { "보호구 확인", "부품·센서 점검", "원인 선택", "설비 정지", "차단 확인", "부품 조치", "재가동", "훈련 완료" };

        private void Awake()
        {
            if (thresholds == null) thresholds = ScriptableObject.CreateInstance<PumpThresholds>();
            random = new System.Random(seed);
            Generate();
        }

        private void Update() { Advance(Time.deltaTime); }

        // [학습용 주석] 센서 생성과 시간 계산을 분리하여 같은 코드로 자동 시험할 수 있습니다.
        public void Advance(float seconds)
        {
            if (seconds < 0) throw new ArgumentOutOfRangeException("seconds");
            if (TrainingActive) Duration += seconds;
            if (Running) faultAge += seconds;
            dataAge += seconds;
            accumulator += seconds;
            if (!FreezeSensors && accumulator >= 1)
            {
                accumulator %= 1;
                Generate();
            }
            else if (Current != null)
            {
                PumpState next = thresholds.Evaluate(Current.sensors, Running, dataAge);
                if (State != next) { State = next; Current.state = State.ToString(); Notify(); }
            }
        }

        private float Noise(float amplitude) { return (float)(random.NextDouble() * 2 - 1) * amplitude; }
        private void Generate()
        {
            float progress = Mathf.Clamp01(faultAge / 12f);
            float temp = 62 + Noise(.8f), vib = 1.9f + Noise(.15f), pressure = 3.8f + Noise(.1f), flow = 50 + Noise(.8f);
            if (Fault == FaultType.Overheat) temp += progress * 38;
            if (Fault == FaultType.Vibration) vib += progress * 7;
            if (Fault == FaultType.LowFlow) { pressure -= progress * 1.8f; flow -= progress * 25; }
            if (!Running) { vib = 0; pressure = 0; flow = 0; }
            dataAge = 0;
            State = thresholds.Evaluate(new SensorValues(temp, vib, pressure, flow), Running, 0);
            Current = new PumpSnapshot { snapshotId = ++sequence, timestamp = DateTimeOffset.Now.ToString("o"),
                isRunning = Running, sensors = new SensorValues(temp, vib, pressure, flow), state = State.ToString(), scenario = Fault.ToString() };
            History.Add(Current);
            if (History.Count > 60) History.RemoveAt(0);
            Notify();
        }

        public void ShowAlarm()
        {
            SensorValues s=Current.sensors;
            Feedback="상태 "+State+" · 온도 "+s.motorTemp.ToString("F1")+"°C · 진동 "+s.vibration.ToString("F2")+"mm/s · 압력 "+s.pressure.ToString("F2")+"bar · 유량 "+s.flow.ToString("F1")+"m³/h";
            Notify();
        }

        public void SetFault(FaultType value)
        {
            if (TrainingActive) { Feedback = "훈련 중에는 시나리오를 바꿀 수 없습니다. 초기화 후 선택하세요."; Notify(); return; }
            Fault = value; faultAge = 0; Running = true; FreezeSensors = false;
            Feedback = value == FaultType.None ? "정상 운전으로 복구했습니다." : FaultName(value) + " 고장을 주입했습니다.";
            Generate();
        }
        public static string FaultName(FaultType value)
        { return value == FaultType.Overheat ? "모터 과열" : value == FaultType.Vibration ? "베어링 이상 진동" : value == FaultType.LowFlow ? "흡입 배관 유량 저하" : "정상"; }
        public static string CauseName(FaultType value)
        { return value == FaultType.Overheat ? "냉각팬 정지" : value == FaultType.Vibration ? "베어링 마모" : "흡입 배관 누설"; }
        public static string PartFor(FaultType value)
        { return value == FaultType.Overheat ? "Fan" : value == FaultType.Vibration ? "Bearing" : "Pipe"; }

        public void ResetSimulation()
        {
            random = new System.Random(seed); Fault = FaultType.None; Running = true; FreezeSensors = false;
            TrainingActive = false; Step = 0; Duration = 0; faultAge = 0; dataAge = 0; accumulator = 0;
            Result = null; SelectedPart = null; Mistakes.Clear(); safetyMisses.Clear(); causeAttempted = false;
            firstCauseCorrect = orderCorrect = true; History.Clear(); Feedback = "동일 seed로 초기화했습니다."; Generate();
        }
        public void ToggleFreeze()
        { FreezeSensors = !FreezeSensors; Feedback = FreezeSensors ? "센서 갱신 정지: 3초 후 미확인 상태를 시험합니다." : "센서 갱신을 재개합니다."; if (!FreezeSensors) Generate(); else Notify(); }

        public void BeginTraining(FaultType value)
        {
            if (value == FaultType.None) return;
            ResetSimulation(); Fault = value; faultAge = 12; TrainingActive = true;
            Feedback = "훈련 시작: 보호구 확인부터 진행하세요."; Generate();
        }

        public void Inspect(string part)
        {
            // UI에서 모델·센서 카드·XR 선택을 같은 부품 정보로 표시합니다.
            SelectedPart = part;
            Feedback = PartLabel(part) + " 선택: 관련 센서와 이상 징후를 확인하세요.";
            if (TrainingActive && Step == 1) { Step = 2; Feedback += " 이제 원인을 선택하세요."; }
            Notify();
        }
        public static string PartLabel(string part)
        { return part == "Motor" ? "모터" : part == "Impeller" ? "임펠러" : part == "Fan" ? "냉각팬" : part == "Bearing" ? "베어링" : part == "Pipe" ? "흡입 배관" : part; }

        public bool ChooseCause(FaultType choice)
        {
            if (!TrainingActive || Step != 2) return Reject("원인 선택", StepNames[1]+"을 먼저 완료하세요.");
            if (!causeAttempted) { firstCauseCorrect = choice == Fault; causeAttempted = true; }
            if (choice != Fault) return Reject(CauseName(choice), "단서와 원인이 일치하지 않습니다.", false);
            Step = 3; Feedback = "원인 판단 완료. 설비를 정지하세요."; Notify(); return true;
        }

        public bool Act(string action, string part = null)
        {
            if (!TrainingActive) { Feedback = "훈련 시나리오를 먼저 시작하세요."; Notify(); return false; }
            string expected = Step == 0 ? "ppe" : Step == 3 ? "stop" : Step == 4 ? "isolate" : Step == 5 ? "repair" : Step == 6 ? "restart" : "inspect/cause";
            if (action == "repair" && Step < 5)
            {
                if (Step == 0) safetyMisses.Add("ppe");
                if (Running) safetyMisses.Add("stop");
                if (Step < 5) safetyMisses.Add("isolate");
                return Reject("부품 조치", "보호구·설비 정지·차단 확인 전에는 조치할 수 없습니다.");
            }
            if (action != expected) return Reject(action, "현재 단계: " + StepNames[Step]);
            if (action == "repair" && part != PartFor(Fault)) return Reject("부품 " + PartLabel(part), "고장 원인에 해당하는 부품을 선택하세요.");
            if (action == "stop") { Running = false; Generate(); }
            if (action == "repair") { Feedback = "가상 " + PartLabel(part) + " 조치를 완료했습니다."; }
            if (action == "restart")
            {
                Running = true; FaultType completedFault = Fault; Fault = FaultType.None; faultAge = 0; Generate();
                Step = 7; TrainingActive = false;
                int accuracy = (firstCauseCorrect ? 30 : 0) + (orderCorrect ? 20 : 0);
                int safety = Mathf.Max(0, 30 - 10 * safetyMisses.Count);
                int timeScore = Mathf.Max(0, 20 - 5 * Mathf.CeilToInt(Mathf.Max(0, Duration - 300) / 30));
                int total = Mathf.Clamp(accuracy + safety + timeScore, 0, 100);
                Result = new TrainingResult { scenarioId = completedFault.ToString(), score = total, accuracy = accuracy, safety = safety,
                    timeScore = timeScore, grade = total >= 90 ? "S" : total >= 75 ? "A" : total >= 60 ? "B" : "C",
                    duration = Duration, completedAt = DateTimeOffset.Now.ToString("o"), mistakes = new List<TrainingMistake>(Mistakes) };
                SaveResult(); Feedback = "훈련 완료: " + total + "점 / " + Result.grade + "등급"; Notify(); return true;
            }
            Step++; Feedback = "완료. 다음 단계: " + StepNames[Step]; Notify(); return true;
        }

        private bool Reject(string action, string detail, bool sequenceError = true)
        {
            if (sequenceError) orderCorrect = false;
            Mistakes.Add(new TrainingMistake { seconds = Duration, step = Step, action = action, expected = StepNames[Mathf.Clamp(Step, 0, 7)],
                detail = detail, snapshotId = Current.snapshotId, sensors = Current.sensors });
            Feedback = detail; Notify(); return false;
        }
        public void SaveResult()
        {
            if (Result == null) return;
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, "TrainingRecords");
                Directory.CreateDirectory(folder);
                LastSavedPath = Path.Combine(folder, "training-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
                File.WriteAllText(LastSavedPath, JsonUtility.ToJson(Result, true));
            }
            catch (Exception e) { LastSavedPath = null; Feedback = "훈련 기록 저장 실패: " + e.Message; Debug.LogError(Feedback); }
        }
        public void Notify() { if (Changed != null) Changed(); }
    }
}
