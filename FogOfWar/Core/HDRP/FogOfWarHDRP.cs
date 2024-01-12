using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System;

//namespace FoW
//{
    [Serializable, VolumeComponentMenu("FogOfWar")]
    public sealed class FogOfWarHDRP : CustomPostProcessVolumeComponent, IPostProcessComponent
    {
        public IntParameter team = new IntParameter(0);
        public BoolParameter fogFarPlane = new BoolParameter(true);
        public FloatParameter outsideFogStrength = new ClampedFloatParameter(1f, 0f, 1f);
        public BoolParameter pointFiltering = new BoolParameter(false);

        [Header("Color")]
        public ColorParameter fogColor = new ColorParameter(Color.clear);
        public TextureParameter fogColorTexture = new TextureParameter(null);
        public BoolParameter fogTextureScreenSpace = new BoolParameter(false);
        public FloatParameter fogColorTextureScale = new FloatParameter(1);
        public FloatParameter fogColorTextureHeight = new FloatParameter(0);
        
        FogOfWarHDRPManager _postProcess = null;
    
        public bool IsActive() => Application.isPlaying && _postProcess != null && _postProcess.isActive && fogColor.value.a > 0.001f;
        public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.BeforePostProcess;
        
        class FogOfWarHDRPManager : FoW.FogOfWarPostProcessManager
        {
            public Material _material;
            CommandBuffer _cmd;
            RTHandle _destination;

            public bool isActive => _material != null;

            public FogOfWarHDRPManager()
            {
                if (_material == null)
                    _material = new Material(FoW.FogOfWarUtils.FindShader("Hidden/FogOfWarHDRP"));
            }

            public void Setup(CommandBuffer cmd, RTHandle source, RTHandle destination)
            {
                _cmd = cmd;
                _destination = destination;
                _material.mainTexture = source;
            }

            public void OnDestroy()
            {
                CoreUtils.Destroy(_material);
                _material = null;
            }

            protected override void SetTexture(int id, Texture value) { _material.SetTexture(id, value); }
            protected override void SetVector(int id, Vector4 value) { _material.SetVector(id, value); }
            protected override void SetColor(int id, Color value) { _material.SetColor(id, value); }
            protected override void SetFloat(int id, float value) { _material.SetFloat(id, value); }
            protected override void SetMatrix(int id, Matrix4x4 value) { _material.SetMatrix(id, value); }
            protected override void SetKeyword(string keyword, bool enabled)
            {
                if (enabled)
                    _material.EnableKeyword(keyword);
                else
                    _material.DisableKeyword(keyword);
            }

            protected override void GetTargetSize(out int width, out int height, out int depth)
            {
                width = _destination.rt.width;
                height = _destination.rt.height;
                depth = _destination.rt.depth;
            }

            protected override void BlitToScreen()
            {
                HDUtils.DrawFullScreen(_cmd, _material, _destination);
            }
        }

        public override void Setup()
        {
            if (_postProcess == null)
                _postProcess = new FogOfWarHDRPManager();
        }

        public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
        {
            _postProcess.Setup(cmd, source, destination);
            _postProcess.team = team.value;
            _postProcess.camera = camera.camera;
            _postProcess.pointFiltering = pointFiltering.value;
            _postProcess.fogFarPlane = fogFarPlane.value;
            _postProcess.outsideFogStrength = outsideFogStrength.value;
            _postProcess.fogColor = fogColor.value;
            _postProcess.fogColorTexture = fogColorTexture.value;
            _postProcess.fogColorTextureScale = fogColorTextureScale.value;
            _postProcess.fogColorTextureHeight = fogColorTextureHeight.value;

            _postProcess.Render();
        }

        public override void Cleanup()
        {
            _postProcess?.OnDestroy();
            _postProcess = null;
        }
    }
//}
