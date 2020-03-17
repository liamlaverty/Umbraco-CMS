﻿using System;
using System.Collections.Generic;
using System.Web;
using Umbraco.Core.Assets;

namespace Umbraco.Core.Runtime
{
    public interface IRuntimeMinifier
    {
        int Version { get; }
        string FileMapDefaultFolder { get; set; }

        //return type HtmlHelper
        string RequiresCss(string filePath, string pathNameAlias);

        //return type IHtmlString
        //IClientDependencyPath[]
        string RenderCssHere(params string[] path);

        // return type HtmlHelper
        string RequiresJs(string filePath);

        // return type IHtmlString
        string RenderJsHere();

        IEnumerable<string> GetAssetPaths(AssetType assetType, List<IAssetFile> attributes);

        string Minify(string src);
    }
}