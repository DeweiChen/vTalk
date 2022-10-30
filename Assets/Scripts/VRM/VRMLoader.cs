using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniGLTF;
using UniHumanoid;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.UIElements;
using VRM;
using VRM.SimpleViewer;
using VRMShaders;
using static UnityEngine.GraphicsBuffer;

namespace vTalk.VRMModel
{
    public partial class VRMLoader : MonoBehaviour
    {
#if UNITY_WEBGL
        [DllImport("__Internal")]
        private static extern void WebGLFileDialog();
#endif

        #region UI

        [SerializeField]
        bool m_enableLipSync = default;

        [SerializeField]
        bool m_enableAutoBlink = default;

        [SerializeField]
        bool m_useUrpMaterial = default;

        [SerializeField]
        bool m_useAsync = default;

        [SerializeField]
        bool m_loadAnimation = default;

        [SerializeField]
        bool m_useFastSpringBone = default;
        #endregion

        [SerializeField]
        HumanPoseTransfer m_src = default;

        [SerializeField]
        GameObject m_target = default;

        [SerializeField]
        TextAsset m_motion;

        [SerializeField]
        HumanPoseClip m_pose = default;

        Loaded m_loaded;

        private void Reset()
        {
            m_src = GameObject.FindObjectOfType<HumanPoseTransfer>();

            m_target = GameObject.FindObjectOfType<TargetMover>().gameObject;
        }

        private void Start()
        {
            // load initial bvh
            if (m_motion != null)
            {
                LoadMotion("tmp.bvh", m_motion.text);
            }

            LoadDefaultVRM();
        }

        private void LoadMotion(string path, string source)
        {
            var context = new UniHumanoid.BvhImporterContext();
            context.Parse(path, source);
            context.Load();
            SetMotion(context.Root.GetComponent<HumanPoseTransfer>());
        }

        private void Update()
        {
            if (m_loaded != null)
            {
                m_loaded.EnableLipSyncValue = m_enableLipSync;
                m_loaded.EnableBlinkValue = m_enableAutoBlink;
                m_loaded.Update();
            }
        }

        IEnumerator LoadTexture(string url)
        {
            var www = new WWW(url);
            yield return www;
            LoadModelAsync("tmp.vrm", www.bytes);
        }

        public void FileSelected(string url)
        {
            Debug.Log($"FileSelected: {url}");
            StartCoroutine(LoadTexture(url));
        }

        void LoadDefaultVRM()
        {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_ANDROID
            var path = Application.streamingAssetsPath + "/default.vrm";
#else
            var path = Application.dataPath + "/default.vrm";
#endif
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(LoadUriAsync(path));
#else
            LoadPathAsync(path);
#endif
        }

        IEnumerator LoadUriAsync(string uri)
        {
            var request = UnityWebRequest.Get(uri);
            yield return request.Send();
            if (request.isNetworkError)
            {
                Debug.Log(request.error);
                yield break;
            }
            var data = request.downloadHandler.data;
            LoadModelAsync(uri, data);
        }

        async void LoadPathAsync(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"{path} not exists");
                return;
            }
            LoadModelAsync(path, File.ReadAllBytes(path));
        }

        async void LoadModelAsync(string path, byte[] bytes)
        {
            var size = bytes != null ? bytes.Length : 0;
            Debug.Log($"LoadModelAsync: {path}: {size}bytes");

            var ext = Path.GetExtension(path).ToLower();
            switch (ext)
            {
                case ".gltf":
                case ".glb":
                case ".zip":
                    {
                        var instance = await GltfUtility.LoadAsync(path,
                            GetIAwaitCaller(m_useAsync),
                            GetGltfMaterialGenerator(m_useUrpMaterial));
                        SetModel(instance);
                        break;
                    }

                case ".vrm":
                    {
                        VrmUtility.MaterialGeneratorCallback materialCallback = (VRM.glTF_VRM_extensions vrm) => GetVrmMaterialGenerator(m_useUrpMaterial, vrm);
                        var instance = await VrmUtility.LoadBytesAsync(path, bytes, GetIAwaitCaller(m_useAsync), materialCallback, null, loadAnimation: m_loadAnimation);
                        SetModel(instance);
                        break;
                    }

                case ".bvh":
                    LoadMotion(path, File.ReadAllText(path));
                    break;
            }
        }

        static IMaterialDescriptorGenerator GetGltfMaterialGenerator(bool useUrp)
        {
            if (useUrp)
            {
                return new GltfUrpMaterialDescriptorGenerator();
            }
            else
            {
                return new GltfMaterialDescriptorGenerator();
            }
        }

        static IMaterialDescriptorGenerator GetVrmMaterialGenerator(bool useUrp, VRM.glTF_VRM_extensions vrm)
        {
            if (useUrp)
            {
                return new VRM.VRMUrpMaterialDescriptorGenerator(vrm);
            }
            else
            {
                return new VRM.VRMMaterialDescriptorGenerator(vrm);
            }
        }

        static IAwaitCaller GetIAwaitCaller(bool useAsync)
        {
            if (useAsync)
            {
#if UNITY_WEBGL
                return new RuntimeOnlyNoThreadAwaitCaller();
#else
                return new RuntimeOnlyAwaitCaller();
#endif
            }
            else
            {
                return new ImmediateCaller();
            }
        }

        void SetModel(RuntimeGltfInstance instance)
        {
            // cleanup
            if (m_loaded != null)
            {
                m_loaded.Dispose();
                m_loaded = null;
            }

            if (m_useFastSpringBone)
            {
                var _ = FastSpringBoneReplacer.ReplaceAsync(instance.Root);
            }

            instance.EnableUpdateWhenOffscreen();
            instance.ShowMeshes();

            m_loaded = new Loaded(instance, m_src, m_target.transform);
        }

        void SetMotion(HumanPoseTransfer src)
        {
            m_src = src;
            src.GetComponent<Renderer>().enabled = false;
            if (m_loaded != null)
            {
                m_loaded.EnableBvh(src);
            }
        }
    }
}
