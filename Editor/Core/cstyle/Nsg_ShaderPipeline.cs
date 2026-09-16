using System.Collections.Generic;
using UnityEngine;

namespace NekoScriptGraph
{
    /// <summary>
    /// Набор различий одной графической管线.
    ///
    /// Именно из-за этих различий Shader Graph не переносится между管线:
    /// он генерирует фиксированный шаблон. Здесь шаблон — данные, поэтому
    /// смена管线 это смена набора, а не правка кода.
    /// </summary>
    public class Nsg_ShaderPipeline
    {
        public string Id;
        public string DisplayName;

        /// <summary>Перевод в пространство отсечения: UnityObjectToClipPos / TransformObjectToHClip / TransformWorldToHClip.</summary>
        public string ClipSpaceCall;

        /// <summary>Шаблон выборки текстуры. {{0}} — имя текстуры, {{1}} — UV.</summary>
        public string SampleCall;

        /// <summary>Имя функции выборки: tex2D или SAMPLE_TEXTURE2D.</summary>
        public string SampleName;

        /// <summary>Сколько аргументов у выборки: 2 у tex2D, 3 у SAMPLE_TEXTURE2D.</summary>
        public int SampleArity;

        /// <summary>Оболочка ShaderLab. %NAME% заменяется именем шейдера.</summary>
        public string ShellTemplate;

        public string Note;
    }

    public static class Nsg_ShaderPipelines
    {
        static Nsg_ShaderPipeline _builtIn;
        static Nsg_ShaderPipeline _urp;
        static Nsg_ShaderPipeline _hdrp;

        public static Nsg_ShaderPipeline BuiltIn
        {
            get
            {
                if (_builtIn == null) _builtIn = BuildBuiltIn();
                return _builtIn;
            }
        }

        public static Nsg_ShaderPipeline Urp
        {
            get
            {
                if (_urp == null) _urp = BuildUrp();
                return _urp;
            }
        }

        public static Nsg_ShaderPipeline Hdrp
        {
            get
            {
                if (_hdrp == null) _hdrp = BuildHdrp();
                return _hdrp;
            }
        }

        public static List<Nsg_ShaderPipeline> All()
        {
            return new List<Nsg_ShaderPipeline> { BuiltIn, Urp, Hdrp };
        }

        public static Nsg_ShaderPipeline Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return BuiltIn;

            switch (id)
            {
                case "urp": return Urp;
                case "hdrp": return Hdrp;
                default: return BuiltIn;
            }
        }

        /// <summary>
        /// Определяет管线 проекта. Проект может быть настроен на URP или HDRP —
        /// тогда и вызовы, и оболочка берутся соответствующие.
        /// </summary>
        public static Nsg_ShaderPipeline Detect()
        {
            try
            {
                var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                if (rp == null) return BuiltIn;

                string type = rp.GetType().FullName ?? string.Empty;
                if (type.IndexOf("Universal", System.StringComparison.OrdinalIgnoreCase) >= 0) return Urp;
                if (type.IndexOf("HD", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    type.IndexOf("RenderPipeline", System.StringComparison.OrdinalIgnoreCase) >= 0) return Hdrp;
                if (type.IndexOf("HighDefinition", System.StringComparison.OrdinalIgnoreCase) >= 0) return Hdrp;
            }
            catch
            {
                // вне редактора или иная настройка — считаем встроенный管线
            }
            return BuiltIn;
        }

        /// <summary>
        /// Активная管线. Ставится слоем редактора при старте и при смене выбора.
        ///
        /// Ядро намеренно НЕ читает ни настройки, ни GraphicsSettings: иначе его
        /// нельзя было бы прогонять вне редактора (тесты), а библиотека блоков
        /// зависела бы от глобального состояния редактора.
        /// </summary>
        public static Nsg_ShaderPipeline Active;

        public static Nsg_ShaderPipeline Current
        {
            get
            {
                if (Active != null) return Active;

                Active = ResolveFromSettings();
                return Active != null ? Active : BuiltIn;
            }
        }

        /// <summary>
        /// Единственное место, где нужны настройки редактора. Вынесено в
        /// отдельный метод: JIT компилирует его только при вызове, поэтому
        /// библиотека блоков HLSL собирается и вне редактора (автономные тесты).
        /// </summary>
        static Nsg_ShaderPipeline ResolveFromSettings()
        {
            string id = Nsg_Settings.Instance.shaderPipeline;
            if (string.IsNullOrEmpty(id) || id == "auto") return Detect();
            return Get(id);
        }

        public static string BuildShell(string shaderName)
        {
            var pipeline = Current;
            string name = string.IsNullOrEmpty(shaderName) ? "New Shader" : shaderName;
            return pipeline.ShellTemplate.Replace("%NAME%", name);
        }

        // ------------------------------------------------------------------

        static Nsg_ShaderPipeline BuildBuiltIn()
        {
            var p = new Nsg_ShaderPipeline
            {
                Id = "builtin",
                DisplayName = "Built-in",
                ClipSpaceCall = "UnityObjectToClipPos",
                SampleCall = "tex2D({{0}}, {{1}})",
                SampleName = "tex2D",
                SampleArity = 2,
                Note = "Встроенный管线, CGPROGRAM и UnityCG.cginc."
            };

            p.ShellTemplate =
                "Shader \"%NAME%\"\n" +
                "{\n" +
                "    Properties\n" +
                "    {\n" +
                "        _MainTex (\"Texture\", 2D) = \"white\" {}\n" +
                "    }\n" +
                "    SubShader\n" +
                "    {\n" +
                "        Tags { \"RenderType\"=\"Opaque\" }\n" +
                "        LOD 100\n" +
                "\n" +
                "        Pass\n" +
                "        {\n" +
                "            CGPROGRAM\n" +
                "            #pragma vertex vert\n" +
                "            #pragma fragment frag\n" +
                "\n" +
                "            #include \"UnityCG.cginc\"\n" +
                "\n" +
                "            struct appdata\n" +
                "            {\n" +
                "                float4 vertex : POSITION;\n" +
                "                float2 uv : TEXCOORD0;\n" +
                "            };\n" +
                "\n" +
                "            struct v2f\n" +
                "            {\n" +
                "                float4 vertex : SV_POSITION;\n" +
                "                float2 uv : TEXCOORD0;\n" +
                "            };\n" +
                "\n" +
                "            sampler2D _MainTex;\n" +
                "            float4 _MainTex_ST;\n" +
                "\n" +
                "            v2f vert (appdata v)\n" +
                "            {\n" +
                "                v2f o;\n" +
                "                o.vertex = UnityObjectToClipPos(v.vertex);\n" +
                "                o.uv = TRANSFORM_TEX(v.uv, _MainTex);\n" +
                "                return o;\n" +
                "            }\n" +
                "\n" +
                "            fixed4 frag (v2f i) : SV_Target\n" +
                "            {\n" +
                "                return tex2D(_MainTex, i.uv);\n" +
                "            }\n" +
                "            ENDCG\n" +
                "        }\n" +
                "    }\n" +
                "}\n";

            return p;
        }

        static Nsg_ShaderPipeline BuildUrp()
        {
            var p = new Nsg_ShaderPipeline
            {
                Id = "urp",
                DisplayName = "URP",
                ClipSpaceCall = "TransformObjectToHClip",
                SampleCall = "SAMPLE_TEXTURE2D({{0}}, sampler{{0}}, {{1}})",
                SampleName = "SAMPLE_TEXTURE2D",
                SampleArity = 3,
                Note = "Universal RP: HLSLPROGRAM, Core.hlsl, макросы TEXTURE2D/SAMPLER."
            };

            p.ShellTemplate =
                "Shader \"%NAME%\"\n" +
                "{\n" +
                "    Properties\n" +
                "    {\n" +
                "        _BaseMap (\"Texture\", 2D) = \"white\" {}\n" +
                "    }\n" +
                "    SubShader\n" +
                "    {\n" +
                "        Tags { \"RenderType\"=\"Opaque\" \"RenderPipeline\"=\"UniversalPipeline\" }\n" +
                "\n" +
                "        Pass\n" +
                "        {\n" +
                "            HLSLPROGRAM\n" +
                "            #pragma vertex vert\n" +
                "            #pragma fragment frag\n" +
                "\n" +
                "            #include \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl\"\n" +
                "\n" +
                "            struct Attributes\n" +
                "            {\n" +
                "                float4 positionOS : POSITION;\n" +
                "                float2 uv : TEXCOORD0;\n" +
                "            };\n" +
                "\n" +
                "            struct Varyings\n" +
                "            {\n" +
                "                float4 positionCS : SV_POSITION;\n" +
                "                float2 uv : TEXCOORD0;\n" +
                "            };\n" +
                "\n" +
                "            TEXTURE2D(_BaseMap);\n" +
                "            SAMPLER(sampler_BaseMap);\n" +
                "\n" +
                "            Varyings vert (Attributes IN)\n" +
                "            {\n" +
                "                Varyings OUT;\n" +
                "                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);\n" +
                "                OUT.uv = IN.uv;\n" +
                "                return OUT;\n" +
                "            }\n" +
                "\n" +
                "            half4 frag (Varyings IN) : SV_Target\n" +
                "            {\n" +
                "                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);\n" +
                "            }\n" +
                "            ENDHLSL\n" +
                "        }\n" +
                "    }\n" +
                "}\n";

            return p;
        }

        static Nsg_ShaderPipeline BuildHdrp()
        {
            var p = new Nsg_ShaderPipeline
            {
                Id = "hdrp",
                DisplayName = "HDRP",
                ClipSpaceCall = "TransformWorldToHClip",
                SampleCall = "SAMPLE_TEXTURE2D({{0}}, sampler{{0}}, {{1}})",
                SampleName = "SAMPLE_TEXTURE2D",
                SampleArity = 3,
                Note = "High Definition RP: HLSLPROGRAM, ShaderVariablesFunctions.hlsl, мировое пространство по умолчанию."
            };

            p.ShellTemplate =
                "Shader \"%NAME%\"\n" +
                "{\n" +
                "    Properties\n" +
                "    {\n" +
                "        _BaseMap (\"Texture\", 2D) = \"white\" {}\n" +
                "    }\n" +
                "    SubShader\n" +
                "    {\n" +
                "        Tags { \"RenderPipeline\"=\"HDRenderPipeline\" \"RenderType\"=\"Opaque\" }\n" +
                "\n" +
                "        Pass\n" +
                "        {\n" +
                "            Name \"Forward\"\n" +
                "            Tags { \"LightMode\"=\"ForwardOnly\" }\n" +
                "\n" +
                "            HLSLPROGRAM\n" +
                "            #pragma vertex vert\n" +
                "            #pragma fragment frag\n" +
                "\n" +
                "            #include \"Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl\"\n" +
                "\n" +
                "            struct Attributes\n" +
                "            {\n" +
                "                float4 positionOS : POSITION;\n" +
                "                float2 uv : TEXCOORD0;\n" +
                "            };\n" +
                "\n" +
                "            struct Varyings\n" +
                "            {\n" +
                "                float4 positionCS : SV_POSITION;\n" +
                "                float2 uv : TEXCOORD0;\n" +
                "            };\n" +
                "\n" +
                "            TEXTURE2D(_BaseMap);\n" +
                "            SAMPLER(sampler_BaseMap);\n" +
                "\n" +
                "            Varyings vert (Attributes IN)\n" +
                "            {\n" +
                "                Varyings OUT;\n" +
                "                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);\n" +
                "                OUT.positionCS = TransformWorldToHClip(positionWS);\n" +
                "                OUT.uv = IN.uv;\n" +
                "                return OUT;\n" +
                "            }\n" +
                "\n" +
                "            half4 frag (Varyings IN) : SV_Target\n" +
                "            {\n" +
                "                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);\n" +
                "            }\n" +
                "            ENDHLSL\n" +
                "        }\n" +
                "    }\n" +
                "}\n";

            return p;
        }
    }
}
