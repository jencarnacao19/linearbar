namespace LinearBar;

/// <summary>
/// User-visible text (pt-PT). Kept in one place so it can be moved to resource files when
/// more languages are added.
/// </summary>
public static class Strings
{
    public const string AppName = "LinearBar";

    // Bar context menu
    public const string TaskManager = "Gestor de Tarefas";
    public const string Customize = "Personalizar a barra…";
    public const string OpenSettings = "Abrir ficheiro de definições";
    public const string OpenLog = "Abrir registo (log)";
    public const string Exit = "Sair do LinearBar";

    // Start button
    public const string Start = "Iniciar";

    // Dock item context menu
    public const string Open = "Abrir";
    public const string NewWindow = "Nova janela";
    public const string Pin = "Fixar no dock";
    public const string Unpin = "Desafixar do dock";
    public const string CloseWindow = "Fechar janela";
    public const string CloseAllWindows = "Fechar todas as janelas";

    // Metrics
    public const string Cpu = "CPU";
    public const string Memory = "RAM";
    public const string Network = "REDE";
    public const string Battery = "Bateria";
    public const string CpuDetail = "Utilização do processador: {0:0} %";
    public const string MemoryDetail = "Memória em uso: {0} de {1} ({2:0} %)";
    public const string NetworkDetail = "Descarga: {0}\nEnvio: {1}";
    public const string BatteryDetail = "Bateria: {0} % ({1})";
    public const string BatteryCharging = "ligado à corrente";
    public const string BatteryDischarging = "a bateria";
    public const string MetricsHint = "Clique para abrir o Gestor de Tarefas";

    // Tray
    public const string ShowHiddenIcons = "Mostrar ícones ocultos";
    public const string HideHiddenIcons = "Ocultar ícones";
}
