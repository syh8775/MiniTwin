using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MiniTwin
{
    public sealed class PumpView : MonoBehaviour
    {
        public MiniTwinCore core;
        public bool xrInteractions;
        public bool enableMouseSelection = true;
        public Transform Rotor { get; private set; }
        private readonly Dictionary<string, Renderer> parts = new Dictionary<string, Renderer>();
        private readonly Color steel = new Color(.42f, .52f, .59f);
        private readonly Color blue = new Color(.13f, .39f, .55f);
        private float transparency = 1;

        private void Start()
        {
            if (core == null) core = FindAnyObjectByType<MiniTwinCore>();
            if (parts.Count == 0) Build();
        }

        // [실제 코드에 남길 주석] 외부 모델 없이 점검 가능한 부품 구조를 도형으로 구성합니다.
        public void Build()
        {
            Add("Base", PrimitiveType.Cube, new Vector3(0, .08f, 0), new Vector3(3.1f, .16f, 1.3f), steel);
            Add("Motor", PrimitiveType.Cylinder, new Vector3(-.55f, .8f, 0), new Vector3(.8f, .58f, .8f), blue, new Vector3(0, 0, 90));
            for (int i = 0; i < 6; i++)
                Add("Fin" + i, PrimitiveType.Cylinder, new Vector3(-.93f + i * .15f, .8f, 0), new Vector3(.9f, .025f, .9f), blue, new Vector3(0, 0, 90));
            Add("Fan", PrimitiveType.Cylinder, new Vector3(-1.23f, .8f, 0), new Vector3(.76f, .08f, .76f), steel, new Vector3(0, 0, 90));
            Add("Bearing", PrimitiveType.Cylinder, new Vector3(.15f, .8f, 0), new Vector3(.34f, .18f, .34f), steel, new Vector3(0, 0, 90));
            Add("Housing", PrimitiveType.Cylinder, new Vector3(.74f, .8f, 0), new Vector3(.99f, .20f, .99f), blue, new Vector3(0, 0, 90));
            Rotor = Add("Impeller", PrimitiveType.Cylinder, new Vector3(1.0f, .8f, 0), new Vector3(.78f, .035f, .78f), steel, new Vector3(0, 0, 90)).transform;
            for (int i = 0; i < 4; i++)
            {
                var blade = Add("Blade" + i, PrimitiveType.Cube, new Vector3(1.05f, .8f, 0), new Vector3(.04f, .56f, .065f), steel);
                blade.transform.SetParent(Rotor, true); blade.transform.RotateAround(Rotor.position, transform.right, i * 45);
            }
            Add("Pipe", PrimitiveType.Cylinder, new Vector3(1.42f, .8f, 0), new Vector3(.28f, .4f, .28f), steel, new Vector3(0, 0, 90));
            Add("Outlet", PrimitiveType.Cylinder, new Vector3(.74f, 1.47f, 0), new Vector3(.28f, .30f, .28f), steel);
            Add("Flange", PrimitiveType.Cylinder, new Vector3(.74f, 1.78f, 0), new Vector3(.45f, .04f, .45f), steel);
            Add("Stand1", PrimitiveType.Cube, new Vector3(-.5f, .37f, 0), new Vector3(.8f, .55f, .6f), steel);
            Add("Stand2", PrimitiveType.Cube, new Vector3(.75f, .35f, 0), new Vector3(.65f, .5f, .6f), steel);
        }

        private Renderer Add(string id, PrimitiveType type, Vector3 p, Vector3 scale, Color color, Vector3 rotation = default(Vector3))
        {
            GameObject obj = GameObject.CreatePrimitive(type);
            obj.name = id; obj.transform.SetParent(transform, false);
            obj.transform.localPosition = p; obj.transform.localScale = scale; obj.transform.localEulerAngles = rotation;
            Renderer renderer = obj.GetComponent<Renderer>();
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = color; material.SetFloat("_Smoothness", .55f); material.SetFloat("_Metallic", .55f);
            renderer.sharedMaterial = material; parts[id] = renderer;
            if (id == "Motor" || id == "Fan" || id == "Bearing" || id == "Impeller" || id == "Pipe")
            {
                var selection = obj.AddComponent<PumpPart>(); selection.id = id; selection.view = this;
                if (xrInteractions)
                {
                    var interactable = obj.AddComponent<XRSimpleInteractable>();
                    interactable.selectEntered.AddListener(_ => SelectPart(id));
                }
            }
            return renderer;
        }
        public void SelectPart(string id) { if (core != null) core.Inspect(id); }
        public void SetTracking(bool tracked)
        { transparency = tracked ? 1 : .25f; foreach (var item in parts) item.Value.enabled = tracked; }

        private void Update()
        {
            if (core == null || core.Current == null) return;
            if (Rotor != null && core.Running) Rotor.Rotate(Vector3.up, core.Current.sensors.flow * 6 * Time.deltaTime, Space.Self);
            foreach (var item in parts)
            {
                Color baseColor = item.Key == "Motor" || item.Key == "Housing" || item.Key.StartsWith("Fin") ? blue : steel;
                if (item.Key == MiniTwinCore.PartFor(core.Fault) && core.Fault != FaultType.None)
                    baseColor = core.State == PumpState.DANGER ? new Color(1, .27f, .23f) : new Color(1, .68f, .22f);
                if (core.State == PumpState.UNKNOWN) baseColor = Color.gray;
                baseColor.a = transparency;
                item.Value.sharedMaterial.color = baseColor;
            }
            if (enableMouseSelection && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                Camera cam = Camera.main;
                if (cam != null && Physics.Raycast(cam.ScreenPointToRay(Mouse.current.position.ReadValue()), out RaycastHit hit, 100))
                {
                    var part = hit.collider.GetComponent<PumpPart>(); if (part != null) SelectPart(part.id);
                }
            }
        }
        private void OnDestroy()
        { foreach (var item in parts) if (item.Value != null && item.Value.sharedMaterial != null) Destroy(item.Value.sharedMaterial); }
    }
    public sealed class PumpPart : MonoBehaviour { public string id; public PumpView view; }
}
