using System.Xml;

namespace Vecxy.UI;

public class UIApp
{
    public UIWindow CreateWindow(string xmlPath)
    {
        // Загружаем XML файл
        var doc = new XmlDocument();
        doc.Load(xmlPath);
        
        // Создаем окно из корневого элемента <Window>
        var windowElement = doc.DocumentElement;
        if (windowElement.Name != "Window")
            throw new InvalidOperationException("Root element must be <Window>");
        
        var window = new UIWindow();
        
        // Устанавливаем атрибуты окна
        if (windowElement.Attributes["title"] != null)
            window.Title = windowElement.Attributes["title"].Value;
            
        if (windowElement.Attributes["class"] != null)
            window.AddClass(windowElement.Attributes["class"].Value);
        
        // Обрабатываем дочерние элементы
        foreach (XmlNode child in windowElement.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element)
            {
                var node = CreateNodeFromXml(child);
                if (node != null)
                    window.AddNode(node);
            }
        }
        
        return window;
    }
    
    private UINode CreateNodeFromXml(XmlNode xmlNode)
    {
        UINode node = new UINode();
        
        // Устанавливаем ID если есть
        if (xmlNode.Attributes["id"] != null)
            node.Id = xmlNode.Attributes["id"].Value;
            
        // Устанавливаем классы если есть
        if (xmlNode.Attributes["class"] != null)
            node.AddClass(xmlNode.Attributes["class"].Value);
        
        // Обрабатываем вложенные элементы
        foreach (XmlNode child in xmlNode.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element)
            {
                if (child.Name == "Text")
                {
                    var textNode = new UIText();
                    if (child.Attributes["value"] != null)
                        textNode.Value = child.Attributes["value"].Value;
                    node.AddNode(textNode);
                }
                else
                {
                    // Рекурсивно создаем обычные узлы
                    var childNode = CreateNodeFromXml(child);
                    node.AddNode(childNode);
                }
            }
        }
        
        return node;
    }
}