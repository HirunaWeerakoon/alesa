using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class RevolverCombat : MonoBehaviour
{
    public RevolverAmmo ammo = new RevolverAmmo();
    [SerializeField] private bool hasRevolver;
    [Min(0.01f)] public float reloadSeconds = 2f;
    [Min(0.01f)] public float shotInterval = 0.35f;
    public float damage = 35f;
    public float range = 60f;
    public LayerMask hitMask = ~0;
    [Header("Weapon presentation")]
    public GameObject weaponPrefab;
    public Material weaponMaterial;
    public Vector3 modelAngles = Vector3.zero;
    [Min(0.1f)] public float modelLength = 0.34f;
    public Vector3 loweredPosition = new Vector3(0.14f, -0.16f, 0.28f);
    public Vector3 aimedPosition = new Vector3(0, -0.08f, 0.24f);
    public Vector3 loweredAngles = new Vector3(5, -8, -4);
    public Vector3 aimedAngles = Vector3.zero;
    [Header("Reload presentation")]
    public Vector3 reloadPosition = new Vector3(0.09f, -0.1f, 0.29f);
    public Vector3 reloadAngles = new Vector3(-12, -55, 25);
    public float cylinderSwingDistance = 0.055f;
    [Tooltip("Optional existing camera-child view; otherwise instantiated from the prefab.")]
    public Transform weaponView;
    [Header("Weapon obstruction")]
    public LayerMask obstructionMask = ~0;
    [Min(0.01f)] public float obstructionRadius = 0.045f;
    [Min(0)] public float obstructionPadding = 0.08f;
    public Vector3 muzzleOffset = new Vector3(0, 0.045f, 0.17f);
    public Vector3 obstructedOffset = new Vector3(0, -0.12f, -0.1f);
    public UnityEvent onShot = new UnityEvent();
    public UnityEvent onReloaded = new UnityEvent();
    public UnityEvent onEmptyClick = new UnityEvent();
    public UnityEvent onReloadStarted = new UnityEvent();
    public UnityEvent onReloadCanceled = new UnityEvent();
    public bool IsTriggerHeld { get; private set; }
    public void SetTriggerHeld(bool held) { IsTriggerHeld = held; }
    public int LoadedAmmo => ammo.loaded;
    public int SpareAmmo => ammo.spare;
    public bool HasRevolver => hasRevolver;
    public bool IsAiming => player != null && player.State.CurrentAction == PlayerState.Action.Aiming;
    public bool IsReloading => player != null && player.State.CurrentAction == PlayerState.Action.Reloading;
    public bool IsObstructed => player != null && player.State.WeaponObstructed;
    public float AimBlend => aimBlend;
    public bool ReadyToFire => player != null && player.State.CanShoot && aimBlend >= 0.95f && obstructionBlend <= 0.05f;
    private PlayerGameplay player;
    private float aimBlend, nextShot, reloadProgress;
    private Coroutine reloadRoutine;
    private Transform cylinderPivot;
    private Vector3 cylinderClosedPosition;
    private float obstructionBlend;
    private readonly RaycastHit[] obstructionHits = new RaycastHit[32];
    private readonly Collider[] obstructionOverlaps = new Collider[32];

    private void Awake()
    {
        player = GetComponent<PlayerGameplay>();
        ammo.Normalize();
        if (GetComponent<RevolverAudio>() == null) gameObject.AddComponent<RevolverAudio>();
    }

    private void Update()
    {
        RefreshObstruction();
        aimBlend = Mathf.MoveTowards(aimBlend, IsAiming ? 1f : 0f, Time.deltaTime * 7f);
        obstructionBlend = Mathf.MoveTowards(obstructionBlend, IsObstructed ? 1f : 0f, Time.deltaTime * 12f);
        if (weaponView != null)
        {
            weaponView.gameObject.SetActive(hasRevolver && !player.BlocksControls);
            float reloadBlend = IsReloading ? ReloadPoseBlend(reloadProgress) : 0f;
            Vector3 position = Vector3.Lerp(loweredPosition, aimedPosition, aimBlend);
            Quaternion rotation = Quaternion.Slerp(Quaternion.Euler(loweredAngles), Quaternion.Euler(aimedAngles), aimBlend);
            weaponView.localPosition = Vector3.Lerp(position, reloadPosition, reloadBlend);
            weaponView.localRotation = Quaternion.Slerp(rotation, Quaternion.Euler(reloadAngles), reloadBlend);
            weaponView.localPosition += obstructedOffset * obstructionBlend;
            weaponView.localRotation *= Quaternion.Euler(30f * obstructionBlend, 0, 0);
            if (cylinderPivot != null)
            {
                float open = IsReloading ? ReloadCylinderBlend(reloadProgress) : 0f;
                cylinderPivot.localPosition = cylinderClosedPosition + Vector3.left * cylinderSwingDistance * open;
                // Spin only while the cylinder is out; finish on a complete revolution.
                float spin = IsReloading ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, reloadProgress)) * 720f : 0f;
                cylinderPivot.localRotation = Quaternion.AngleAxis(spin, Vector3.forward);
            }
        }
    }

    private void Start() { if (hasRevolver && weaponView == null) CreateWeaponView(); }

    public void Equip(int rounds)
    {
        if (hasRevolver) { AddAmmo(rounds); return; }
        hasRevolver = true;
        ammo.LoadPickup(rounds);
        if (weaponView == null) CreateWeaponView();
        player.Notify("Revolver acquired. Hold RMB to aim; LMB to fire; R to reload.");
    }

    public void AddAmmo(int amount) { ammo.AddSpare(amount); }

    public void SetAim(bool held)
    {
        if (player == null) return;
        player.State.SetAiming(held && hasRevolver);
        if (player != null && player.Motor != null) player.Motor.SetAiming(IsAiming);
    }

    public bool TryShoot()
    {
        RefreshObstruction();
        if (!ReadyToFire || player.BlocksControls || Time.time < nextShot) return false;
        nextShot = Time.time + shotInterval;
        if (!ammo.TryConsume()) { onEmptyClick.Invoke(); player.Notify("Cylinder empty. Press R to reload."); return false; }
        Vector3 aimPoint = player.View.transform.position + player.View.transform.forward * range;
        if (player.RaycastFromView(range, hitMask, out RaycastHit aimedHit)) aimPoint = aimedHit.point;
        Vector3 muzzle = weaponView != null ? weaponView.TransformPoint(muzzleOffset) : player.View.transform.position;
        Vector3 trajectory = aimPoint - muzzle;
        // Resolve the shot from the actual muzzle towards the crosshair target so
        // a wall beside the camera still blocks the projectile path.
        if (player.RaycastFromPoint(muzzle, trajectory.normalized, Mathf.Min(range, trajectory.magnitude) + 0.01f, hitMask, out RaycastHit hit))
        {
            var boss = hit.collider.GetComponentInParent<PossessedBoss>();
            if (boss != null) boss.HitBySilverBullet();
            else foreach (var behaviour in hit.collider.GetComponentsInParent<MonoBehaviour>())
                if (behaviour is IDamageable target) { target.TakeDamage(damage); break; }
        }
        player.Notify("Shot fired", 0.3f);
        onShot.Invoke();
        return true;
    }

    public bool TryReload()
    {
        if (!hasRevolver || ammo.loaded >= ammo.capacity || ammo.spare == 0 || !player.State.TryStartReload()) return false;
        reloadRoutine = StartCoroutine(Reload());
        return true;
    }

    private IEnumerator Reload()
    {
        reloadProgress = 0f;
        SetAim(false);
        onReloadStarted.Invoke();
        float duration = Mathf.Max(0.01f, reloadSeconds);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            reloadProgress = Mathf.Clamp01(elapsed / duration);
            yield return null;
            elapsed += Time.deltaTime;
        }
        ammo.Reload();
        reloadProgress = 1f;
        player.State.EndReload(); reloadRoutine = null;
        onReloaded.Invoke();
    }

    public void CancelReload()
    {
        bool wasReloading = IsReloading || reloadRoutine != null;
        if (reloadRoutine != null) StopCoroutine(reloadRoutine);
        reloadRoutine = null; player?.State.EndReload(); reloadProgress = 0f;
        if (cylinderPivot != null)
        {
            cylinderPivot.localPosition = cylinderClosedPosition;
            cylinderPivot.localRotation = Quaternion.identity;
        }
        if (wasReloading) onReloadCanceled.Invoke();
    }

    private bool ForeignOverlap(Vector3 point)
    {
        int count = Physics.OverlapSphereNonAlloc(point, obstructionRadius, obstructionOverlaps, obstructionMask, QueryTriggerInteraction.Ignore);
        if (count == obstructionOverlaps.Length) return true;
        for (int i = 0; i < count; i++) if (!obstructionOverlaps[i].transform.IsChildOf(transform)) return true;
        return false;
    }

    private void RefreshObstruction()
    {
        if (player == null || player.View == null) return;
        if (!hasRevolver || player.BlocksControls) { player.State.SetWeaponObstructed(false); return; }
        var camera = player.View.transform;
        // Probe the intended pose, not the retracted pose, to avoid oscillating.
        var position = Vector3.Lerp(loweredPosition, aimedPosition, aimBlend);
        var rotation = Quaternion.Slerp(Quaternion.Euler(loweredAngles), Quaternion.Euler(aimedAngles), aimBlend);
        Vector3 intendedMuzzle = camera.TransformPoint(position + rotation * muzzleOffset);
        Vector3 path = intendedMuzzle - camera.position;
        bool blocked = ForeignOverlap(camera.position) || ForeignOverlap(intendedMuzzle);
        if (!blocked)
        {
            int count = Physics.SphereCastNonAlloc(camera.position, obstructionRadius, path.normalized,
                obstructionHits, path.magnitude + obstructionPadding, obstructionMask, QueryTriggerInteraction.Ignore);
            blocked = count == obstructionHits.Length;
            for (int i = 0; i < count && !blocked; i++) blocked = !obstructionHits[i].collider.transform.IsChildOf(transform);
        }
        player.State.SetWeaponObstructed(blocked);
    }

    private static float ReloadPoseBlend(float progress)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.2f)) *
            (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1f, progress)));
    }

    private static float ReloadCylinderBlend(float progress)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.3f, progress)) *
            (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 0.85f, progress)));
    }

    private void OnDisable() { CancelReload(); if (player != null) SetAim(false); }

    private void CreateWeaponView()
    {
        var root = new GameObject("Revolver view");
        root.transform.SetParent(player.View.transform, false);
        weaponView = root.transform;
        weaponView.localPosition = loweredPosition;
        weaponView.localRotation = Quaternion.Euler(loweredAngles);
        if (weaponPrefab != null)
        {
            var model = RevolverModel.Create(weaponPrefab, weaponView, modelAngles, modelLength, weaponMaterial, true);
            PrepareCylinder(model);
            return;
        }
        // Retain a fallback for player prefabs without a presentation asset assigned.
        Part(root.transform, "Barrel", new Vector3(0, 0.025f, 0.09f), new Vector3(0.045f, 0.05f, 0.24f));
        Part(root.transform, "Cylinder", Vector3.zero, new Vector3(0.085f, 0.08f, 0.1f));
        Part(root.transform, "Grip", new Vector3(0, -0.075f, -0.04f), new Vector3(0.05f, 0.13f, 0.07f));
    }

    private void PrepareCylinder(GameObject model)
    {
        foreach (var child in model.GetComponentsInChildren<Transform>(true))
        {
            if (child == model.transform || !child.name.ToLowerInvariant().Contains("cyl")) continue;
            var pivot = new GameObject("Reload cylinder pivot");
            pivot.transform.SetParent(weaponView, false);
            var renderer = child.GetComponent<Renderer>();
            pivot.transform.position = renderer != null ? renderer.bounds.center : child.position;
            cylinderPivot = pivot.transform;
            cylinderClosedPosition = cylinderPivot.localPosition;
            child.SetParent(cylinderPivot, true);
            break;
        }
    }

    private static void Part(Transform parent, string name, Vector3 position, Vector3 scale)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name; part.transform.SetParent(parent, false);
        part.transform.localPosition = position; part.transform.localScale = scale;
        var collider = part.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        part.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
