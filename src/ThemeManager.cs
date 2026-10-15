using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace VideoStudio;

public sealed record StudioTheme(string Id,string Name,string Description,bool Dark,string Top,string Bottom,string Surface,string Ink,string Muted,string Accent,string Glow);

public static class ThemeManager
{
    public static readonly StudioTheme[] Themes = {
        new("liquid-gray","Liquid Glass Gri","Gümüş yansımalar · sakin ve saydam",false,"#E5E8ED","#BFC7D2","#B8FFFFFF","#252C38","#566174","#405676","#D0DBEF"),
        new("night-glass","Gece Camı","Grafit yüzeyler · geceye uyumlu",true,"#252A34","#10141C","#C92A303C","#F1F3F8","#ADB8CA","#B8CFF5","#414B6B"),
        new("aurora","Aurora","Leylak ve mavi · yumuşak bir ışıltı",true,"#302D46","#181C30","#BA35364E","#F5F2FF","#C0BBD4","#D7BDFF","#63568D"),
        new("porcelain","Porselen","Sıcak beyaz · yalın ve aydınlık",false,"#FAF8F4","#E4E1DC","#DFFFFFFF","#33342F","#676B64","#536653","#DBE5D4")
    };
    public static StudioTheme Current { get; private set; }=Themes[0];
    public static string Normalize(string? id)=>Array.Find(Themes,t=>t.Id==id)?.Id??Themes[0].Id;
    public static void Apply(string? id)
    {
        Current=Array.Find(Themes,t=>t.Id==Normalize(id))!;
        var t=Current; var r=Application.Current.Resources;
        void Set(string key,string color)=>r[key]=Brush(color);
        Set("Bg",t.Top); Set("Text",t.Ink); Set("Muted",t.Muted); Set("Accent",t.Accent); Set("Mint",t.Dark?"#9DDBBC":"#287451");
        Set("Panel",t.Surface); Set("Sidebar",t.Dark?"#93212833":"#79FFFFFF"); Set("Raised",t.Dark?"#C93B4150":"#BBFFFFFF");
        Set("Input",t.Dark?"#9C171D29":"#BFFFFFFF"); Set("Line",t.Dark?"#546B768C":"#8197A5B9");
        Set("Hairline",t.Dark?"#3AFFFFFF":"#D9FFFFFF"); Set("Key",t.Dark?"#9F293140":"#B3F4F6F9");
        Set("KeyAssigned",t.Dark?"#DD424B60":"#E3E2E8F1"); Set("KeySelected",t.Dark?"#DD52617C":"#F1FFFFFF");
        Set("Selection",t.Dark?"#714D628C":"#486F8CAE"); Set("PrimaryText",t.Dark?"#172031":"#FFFFFF");
        Set("Danger",t.Dark?"#FFBAC3":"#A23648"); Set("DangerBg",t.Dark?"#573B2B38":"#A3FFF0F2");
        Set("Glow",t.Glow); Set("TraySurface",t.Dark?"#C51B2130":"#73CDD5E1");
        r["Panel"]=new LinearGradientBrush(new GradientStopCollection {
            new(Brush(t.Dark?"#CB394151":"#DBFFFFFF").Color,0),new(Brush(t.Surface).Color,.40),new(Brush(t.Dark?"#B3202733":"#A9F3F6FA").Color,1) },new Point(0,0),new Point(1,1));
        r["Backdrop"]=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(t.Top),(Color)ColorConverter.ConvertFromString(t.Bottom),48);
        r["GlassEdge"]=new LinearGradientBrush(new GradientStopCollection {
            new(Brush(t.Dark?"#6BFFFFFF":"#F8FFFFFF").Color,0),new(Brush(t.Dark?"#237E8AA3":"#728B9AB0").Color,.55),new(Brush(t.Dark?"#536A758B":"#DAFFFFFF").Color,1) },new Point(0,0),new Point(1,1));
        foreach(Window window in Application.Current.Windows) UpdateCaption(window);
    }
    public static SolidColorBrush Brush(string value)=>new((Color)ColorConverter.ConvertFromString(value));
    public static SolidColorBrush Get(string key)=>(SolidColorBrush)Application.Current.FindResource(key);
    public static void Bind(FrameworkElement element,DependencyProperty property,string key)=>element.SetResourceReference(property,key);
    public static TextBlock Text(string text,double size=13,string brush="Text") {
        var block=new TextBlock { Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap }; Bind(block,TextBlock.ForegroundProperty,brush); return block;
    }
    public static void Initialize()
    {
        Apply("liquid-gray");
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>UpdateCaption((Window)sender)));
    }
    static void UpdateCaption(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle; if(handle==IntPtr.Zero) return;
        int dark=Current.Dark?1:0; DwmSetWindowAttribute(handle,20,ref dark,4);
        var bg=Brush(Current.Top).Color; int color=bg.R|(bg.G<<8)|(bg.B<<16); DwmSetWindowAttribute(handle,35,ref color,4);
        var fg=Brush(Current.Ink).Color; int ink=fg.R|(fg.G<<8)|(fg.B<<16); DwmSetWindowAttribute(handle,36,ref ink,4);
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
}
