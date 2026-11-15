using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BvhJoint
{
    public string name;
    [System.NonSerialized]
    public BvhJoint parent;
    [System.NonSerialized]
    public List<BvhJoint> children = new List<BvhJoint>();
    public Vector3 offset;
    public List<string> channels = new List<string>();
    public GameObject go;
}

public class BvhData
{
    public BvhJoint root;
    public int frameCount;
    public float frameTime;
    public float[][] frames;
}

public class BvhPlayer : MonoBehaviour
{
    [Header("BVH 设置（适配你这份文件）")]
    public TextAsset bvhFile;
    public bool autoPlay = true;
    [Tooltip("开启：关闭根位移，原地播放动画")]
    public bool disableRootMotion = true;
    public float rootPosScale = 0.01f;

    [Header("调试")]
    public bool drawDebugLines = true;

    [Header("UI")]
    public Button playPauseBtn;
    public TextMeshProUGUI btnText;
    public Slider speedSlider;
    public TextMeshProUGUI speedText;

    private BvhData bvh;
    private bool isPlaying;
    private float animTime;
    private float playSpeed = 1f;
    private int currentFrame;

    void Start()
    {
        if (bvhFile != null)
        {
            LoadBvh(bvhFile.text);
            if (bvh != null && bvh.root != null)
            {
                Debug.Log($"✅ BVH 加载成功 | 总帧数：{bvh.frameCount} | 单帧时间：{bvh.frameTime}");
                BuildSkeleton();
            }
            else
            {
                Debug.LogError("❌ BVH 解析失败，root 为空！");
            }
        }
        else
        {
            Debug.LogError("❌ BvhFile 为空！");
        }

        if (playPauseBtn != null)
            playPauseBtn.onClick.AddListener(TogglePlay);

        if (speedSlider != null)
        {
            speedSlider.minValue = 0.1f;
            speedSlider.maxValue = 3.0f;
            speedSlider.value = 1.0f;
            speedSlider.onValueChanged.AddListener(v =>
            {
                playSpeed = v;
                if (speedText != null) speedText.text = $"Speed: {v:F2}";
            });
        }

        isPlaying = autoPlay;
        RefreshBtnText();
    }

    void Update()
    {
        if (!isPlaying || bvh == null || bvh.frames == null || bvh.frames.Length == 0)
            return;

        animTime += Time.deltaTime * playSpeed;
        float total = bvh.frameCount * bvh.frameTime;
        if (animTime > total) animTime = 0;

        currentFrame = Mathf.FloorToInt(animTime / bvh.frameTime);
        if (currentFrame >= bvh.frames.Length)
            currentFrame = bvh.frames.Length - 1;

        ApplyFrame(bvh.frames[currentFrame]);

        if (drawDebugLines) DrawDebugBones(bvh.root);
    }

    void OnDrawGizmosSelected()
    {
        if (bvh == null || bvh.root == null) return;
        Gizmos.color = Color.cyan;
        DrawGizmoBones(bvh.root);
    }

    public void TogglePlay()
    {
        isPlaying = !isPlaying;
        RefreshBtnText();
    }

    void RefreshBtnText()
    {
        if (btnText != null)
            btnText.text = isPlaying ? "Pause" : "Play";
    }

    void LoadBvh(string text)
    {
        var sr = new StringReader(text);
        string line;
        var stack = new Stack<BvhJoint>();
        BvhJoint root = null;
        BvhJoint current = null;
        bool motion = false;
        int frameCount = 0;
        float frameTime = 0f;
        var frameList = new List<float[]>();

        while ((line = sr.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens[0] == "HIERARCHY") continue;
            if (tokens[0] == "MOTION")
            {
                motion = true;
                continue;
            }

            if (!motion)
            {
                if (tokens[0] == "ROOT" || tokens[0] == "JOINT")
                {
                    var j = new BvhJoint { name = tokens[1] };
                    if (current != null)
                    {
                        j.parent = current;
                        current.children.Add(j);
                    }
                    if (root == null) root = j;
                    stack.Push(j);
                    current = j;
                }
                else if (tokens[0] == "End" && tokens[1] == "Site")
                {
                    var j = new BvhJoint { name = "EndSite" };
                    j.parent = current;
                    current.children.Add(j);
                    stack.Push(j);
                    current = j;
                }
                else if (tokens[0] == "OFFSET")
                {
                    current.offset = new Vector3(
                        float.Parse(tokens[1]),
                        float.Parse(tokens[2]),
                        float.Parse(tokens[3])
                    );
                }
                else if (tokens[0] == "CHANNELS")
                {
                    int n = int.Parse(tokens[1]);
                    for (int i = 0; i < n; i++)
                        current.channels.Add(tokens[2 + i].ToLower());
                }
                else if (tokens[0] == "}")
                {
                    stack.Pop();
                    current = stack.Count > 0 ? stack.Peek() : null;
                }
            }
            else
            {
                if (tokens[0] == "Frames:")
                {
                    frameCount = int.Parse(tokens[1]);
                }
                else if (tokens[0] == "Frame" && tokens[1] == "Time:")
                {
                    frameTime = float.Parse(tokens[2]);
                }
                else
                {
                    float[] f = new float[tokens.Length];
                    for (int i = 0; i < tokens.Length; i++)
                        f[i] = float.Parse(tokens[i]);
                    frameList.Add(f);
                }
            }
        }

        bvh = new BvhData
        {
            root = root,
            frameCount = frameCount,
            frameTime = frameTime,
            frames = frameList.ToArray()
        };
    }

    void BuildSkeleton()
    {
        DestroySkeleton(bvh.root);
        BuildJoint(bvh.root, null);
    }

    void BuildJoint(BvhJoint j, GameObject parentGo)
    {
        j.go = new GameObject(j.name);
        if (parentGo != null)
            j.go.transform.SetParent(parentGo.transform, false);
        j.go.transform.localPosition = j.offset;

        foreach (var child in j.children)
            BuildJoint(child, j.go);
    }

    void DestroySkeleton(BvhJoint j)
    {
        if (j.go != null) Destroy(j.go);
        foreach (var c in j.children) DestroySkeleton(c);
    }

    void ApplyFrame(float[] data)
    {
        int ptr = 0;
        ApplyJoint(bvh.root, data, ref ptr, true);
    }

    void ApplyJoint(BvhJoint j, float[] data, ref int ptr, bool isRoot)
    {
        // 临时存位置、旋转
        Vector3 pos = j.go.transform.localPosition;
        float zRot = 0, yRot = 0, xRot = 0;

        foreach (var ch in j.channels)
        {
            float v = data[ptr++];
            switch (ch)
            {
                case "xposition":
                    if (!(isRoot && disableRootMotion))
                        pos.x = v * (isRoot ? rootPosScale : 1f);
                    break;
                case "yposition":
                    if (!(isRoot && disableRootMotion))
                        pos.y = v * (isRoot ? rootPosScale : 1f);
                    break;
                case "zposition":
                    if (!(isRoot && disableRootMotion))
                        pos.z = v * (isRoot ? rootPosScale : 1f);
                    break;
                case "zrotation":
                    zRot = v;
                    break;
                case "yrotation":
                    yRot = v;
                    break;
                case "xrotation":
                    xRot = v;
                    break;
            }
        }

        j.go.transform.localPosition = pos;
        // ✅ 严格按照这份 BVH：先 Z，再 Y，再 X
        j.go.transform.localEulerAngles = new Vector3(xRot, yRot, zRot);

        foreach (var child in j.children)
            ApplyJoint(child, data, ref ptr, false);
    }

    void DrawGizmoBones(BvhJoint j)
    {
        foreach (var child in j.children)
        {
            if (j.go != null && child.go != null)
                Gizmos.DrawLine(j.go.transform.position, child.go.transform.position);
            DrawGizmoBones(child);
        }
    }

    void DrawDebugBones(BvhJoint j)
    {
        foreach (var child in j.children)
        {
            if (j.go != null && child.go != null)
                Debug.DrawLine(j.go.transform.position, child.go.transform.position, Color.cyan);
            DrawDebugBones(child);
        }
    }
}
