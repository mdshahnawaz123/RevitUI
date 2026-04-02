using Autodesk.Revit.DB;

namespace RevitUI.UI
{
    /// <summary>
    /// Wraps a MEP element from either the host document or a linked model,
    /// carrying the transform needed to place its geometry in host coordinates.
    /// </summary>
    public class LinkedElementInfo
    {
        public Element Element { get; }
        public Transform LinkTransform { get; }
        public string LinkName { get; }
        public bool IsLinked { get; }

        /// <summary>Host-model MEP element.</summary>
        public LinkedElementInfo(Element element)
        {
            Element = element;
            LinkTransform = Transform.Identity;
            LinkName = "Host";
            IsLinked = false;
        }

        /// <summary>Linked-model MEP element.</summary>
        public LinkedElementInfo(Element element, Transform linkTransform, string linkName)
        {
            Element = element;
            LinkTransform = linkTransform;
            LinkName = linkName;
            IsLinked = true;
        }

        public string DisplayLabel => IsLinked ? LinkName : "Host";
    }
}