using System.Collections.Generic;

namespace NekoScriptGraph
{
    /// <summary>
    /// Язык HLSL. Устанавливаемый: лежит в LanguageSupport вместе с движком
    /// C-семейства, поэтому если шейдеры не нужны — папку можно удалить.
    ///
    /// Зависит от管线: и вызов перевода в clip space, и макрос выборки текстуры
    /// в URP/HDRP отличаются от встроенного管线.
    /// </summary>
    public class Nsg_HlslLanguage : Nsg_CStyleLanguageBase
    {
        static Nsg_LanguageProfile _profile;

        protected override Nsg_LanguageProfile Profile
        {
            get
            {
                if (_profile == null) _profile = Build();
                return _profile;
            }
        }

        protected override void AddLanguageBlocks(List<NsgBlockDef> into)
        {
            var pipeline = Nsg_ShaderPipelines.Current;
            if (pipeline == null) pipeline = Nsg_ShaderPipelines.BuiltIn;

            into.Add(Nsg_CStyleBlocks.Call("hlsl.clipPos", "expression", pipeline.ClipSpaceCall, "clip space of {0}",
                Nsg_CStyleBlocks.E("position")));

            if (pipeline.SampleArity == 3)
            {
                into.Add(Nsg_CStyleBlocks.Call("hlsl.sample", "expression", pipeline.SampleName, "sample {0} with {1} at {2}",
                    Nsg_CStyleBlocks.E("texture"), Nsg_CStyleBlocks.E("sampler"), Nsg_CStyleBlocks.E("uv")));
            }
            else
            {
                into.Add(Nsg_CStyleBlocks.Call("hlsl.sample", "expression", pipeline.SampleName, "sample {0} at {1}",
                    Nsg_CStyleBlocks.E("sampler"), Nsg_CStyleBlocks.E("uv")));
            }

            AddMath(into);
            AddIntrinsics(into);
        }

        /// <summary>
        /// Остальные встроенные функции HLSL: интерполяция, геометрия,
        /// тригонометрия, производные и работа с текстурами. Все — настоящие
        /// вызовы, поэтому обратимы: разборщик узнаёт их обратно.
        /// </summary>
        static void AddIntrinsics(List<NsgBlockDef> into)
        {
            // --- интерполяция и пороги ---
            into.Add(Nsg_CStyleBlocks.Call("hlsl.smoothstep", "expression", "smoothstep", "smoothstep {0}..{1} at {2}",
                Nsg_CStyleBlocks.E("min"), Nsg_CStyleBlocks.E("max"), Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.ceil", "expression", "ceil", "ceil {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.round", "expression", "round", "round {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.trunc", "expression", "trunc", "truncate {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.sign", "expression", "sign", "sign of {0}", Nsg_CStyleBlocks.E("value")));

            // --- геометрия ---
            into.Add(Nsg_CStyleBlocks.Call("hlsl.distance", "expression", "distance", "distance {0}→{1}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.cross", "expression", "cross", "cross {0}×{1}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.reflect", "expression", "reflect", "reflect {0} about {1}",
                Nsg_CStyleBlocks.E("incident"), Nsg_CStyleBlocks.E("normal")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.refract", "expression", "refract", "refract {0} by {1} ratio {2}",
                Nsg_CStyleBlocks.E("incident"), Nsg_CStyleBlocks.E("normal"),
                Nsg_CStyleBlocks.E("eta")));

            // --- тригонометрия ---
            into.Add(Nsg_CStyleBlocks.Call("hlsl.sin", "expression", "sin", "sine {0}", Nsg_CStyleBlocks.E("angle")));
            into.Add(Nsg_CStyleBlocks.Call("hlsl.cos", "expression", "cos", "cosine {0}", Nsg_CStyleBlocks.E("angle")));
            into.Add(Nsg_CStyleBlocks.Call("hlsl.tan", "expression", "tan", "tangent {0}", Nsg_CStyleBlocks.E("angle")));
            into.Add(Nsg_CStyleBlocks.Call("hlsl.asin", "expression", "asin", "arcsine {0}", Nsg_CStyleBlocks.E("value")));
            into.Add(Nsg_CStyleBlocks.Call("hlsl.acos", "expression", "acos", "arccosine {0}", Nsg_CStyleBlocks.E("value")));
            into.Add(Nsg_CStyleBlocks.Call("hlsl.atan", "expression", "atan", "arctangent {0}", Nsg_CStyleBlocks.E("value")));
            into.Add(Nsg_CStyleBlocks.Call("hlsl.atan2", "expression", "atan2", "arctangent {0}/{1}",
                Nsg_CStyleBlocks.E("y"), Nsg_CStyleBlocks.E("x")));

            // --- степень и логарифмы ---
            into.Add(Nsg_CStyleBlocks.Call("hlsl.exp", "expression", "exp", "e to the power {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.log", "expression", "log", "natural log {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.log2", "expression", "log2", "log base 2 of {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.sqrt", "expression", "sqrt", "square root {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.rsqrt", "expression", "rsqrt", "inverse square root {0}",
                Nsg_CStyleBlocks.E("value")));

            // --- производные ---
            into.Add(Nsg_CStyleBlocks.Call("hlsl.ddx", "expression", "ddx", "screen-space dx of {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.ddy", "expression", "ddy", "screen-space dy of {0}",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.fmod", "expression", "fmod", "float modulo {0} {1}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.mad", "expression", "mad", "multiply-add {0}×{1}+{2}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b"), Nsg_CStyleBlocks.E("c")));

            // --- текстурные выборки и отсечение ---
            into.Add(Nsg_CStyleBlocks.Call("hlsl.tex2Dlod", "expression", "tex2Dlod", "sample {0} at {1}",
                Nsg_CStyleBlocks.E("sampler"), Nsg_CStyleBlocks.E("uv")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.texCUBE", "expression", "texCUBE", "sample cube {0} along {1}",
                Nsg_CStyleBlocks.E("sampler"), Nsg_CStyleBlocks.E("dir")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.clip", "statement", "clip", "clip {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Template("hlsl.discard", "statement", "discard pixel",
                "discard;"));

            // --- матрицы и проверки ---
            into.Add(Nsg_CStyleBlocks.Call("hlsl.determinant", "expression", "determinant", "determinant of {0}",
                Nsg_CStyleBlocks.E("matrix")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.transpose", "expression", "transpose", "transpose {0}",
                Nsg_CStyleBlocks.E("matrix")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.all", "expression", "all", "all of {0} true",
                Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.any", "expression", "any", "any of {0} true",
                Nsg_CStyleBlocks.E("value")));
        }

        static void AddMath(List<NsgBlockDef> into)
        {
            into.Add(Nsg_CStyleBlocks.Call("hlsl.tex2D", "expression", "tex2D", "sample {0} at {1}",
                Nsg_CStyleBlocks.E("sampler"), Nsg_CStyleBlocks.E("uv")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.saturate", "expression", "saturate", "saturate({0})", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.dot", "expression", "dot", "dot {0}·{1}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.normalize", "expression", "normalize", "normalize {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.lerp", "expression", "lerp", "lerp {0}→{1} by {2}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b"), Nsg_CStyleBlocks.E("t")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.clamp", "expression", "clamp", "clamp {0} to {1}..{2}",
                Nsg_CStyleBlocks.E("value"), Nsg_CStyleBlocks.E("min"), Nsg_CStyleBlocks.E("max")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.mul", "expression", "mul", "matrix multiply {0}×{1}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.length", "expression", "length", "length {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.pow", "expression", "pow", "power {0}^{1}",
                Nsg_CStyleBlocks.E("value"), Nsg_CStyleBlocks.E("power")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.abs", "expression", "abs", "abs {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.floor", "expression", "floor", "floor {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.frac", "expression", "frac", "frac {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.step", "expression", "step", "step {0} at {1}",
                Nsg_CStyleBlocks.E("edge"), Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.max", "expression", "max", "max {0} {1}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.min", "expression", "min", "min {0} {1}",
                Nsg_CStyleBlocks.E("a"), Nsg_CStyleBlocks.E("b")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.rcp", "expression", "rcp", "reciprocal {0}", Nsg_CStyleBlocks.E("value")));

            into.Add(Nsg_CStyleBlocks.Call("hlsl.exp2", "expression", "exp2", "2 to the power {0}", Nsg_CStyleBlocks.E("value")));
        }

        static Nsg_LanguageProfile Build()
        {
            var p = new Nsg_LanguageProfile
            {
                Id = "hlsl",
                DisplayName = "HLSL",
                IconName = "HLSL",
                Extensions = new[] { ".hlsl", ".shader", ".cginc", ".compute" },
                Preprocessor = true,
                HasForeach = false,
                HasNew = false
            };

            Nsg_LanguageProfile.Fill(p.Keywords,
                "break", "case", "cbuffer", "const", "continue", "default", "discard",
                "do", "double", "else", "extern", "false", "float", "for", "half",
                "if", "in", "inline", "inout", "int", "matrix", "out", "pass",
                "return", "register", "sampler", "sampler2D", "sampler3D", "samplerCUBE",
                "static", "string", "struct", "switch", "technique", "texture",
                "texture2D", "texture3D", "textureCube", "true", "typedef", "uint",
                "uniform", "unsigned", "vector", "void", "volatile", "while");

            Nsg_LanguageProfile.Fill(p.TypeKeywords,
                "float", "float2", "float3", "float4", "float2x2", "float3x3", "float4x4",
                "half", "half2", "half3", "half4", "double", "int", "uint", "bool",
                "matrix", "vector", "void", "sampler", "sampler2D", "sampler3D",
                "samplerCUBE", "texture2D", "texture3D", "textureCube");

            Nsg_LanguageProfile.Fill(p.StatementKeywords,
                "if", "else", "for", "while", "do", "switch", "case", "default",
                "break", "continue", "return", "discard");

            Nsg_LanguageProfile.Fill(p.TypeDeclKeywords, "struct", "cbuffer");

            Nsg_LanguageProfile.Fill(p.ContainerKeywords,
                "Shader", "Properties", "SubShader", "Pass", "Tags", "Fallback",
                "GrabPass", "UsePass", "Category", "cbuffer");

            return p;
        }
    }
}
