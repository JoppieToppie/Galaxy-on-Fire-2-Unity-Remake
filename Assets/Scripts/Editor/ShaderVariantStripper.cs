// ShaderVariantStripper.cs
// Player builds leave out the shader variants this game can't use (Data.ShaderVariantRules has the rules and why), which is
// most of what a build compiles. Only builds: the Editor compiles what it draws.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    class ShaderVariantStripper : IPreprocessShaders
    {
        public int callbackOrder => 0;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (ShaderVariantRules.UnusedShader(shader.name))
            {
                data.Clear();
                return;
            }
            var target = EditorUserBuildSettings.activeBuildTarget;
            bool vr = target != BuildTarget.StandaloneLinux64 && target != BuildTarget.Android && target != BuildTarget.iOS;
            for (int i = data.Count - 1; i >= 0; i--)
                foreach (var keyword in data[i].shaderKeywordSet.GetShaderKeywords())
                    if (ShaderVariantRules.UnusedKeyword(keyword.name, vr))
                    {
                        data.RemoveAt(i);
                        break;
                    }
        }
    }
}
