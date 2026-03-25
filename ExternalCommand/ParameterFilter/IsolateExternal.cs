using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitUI.ExternalCommand.ParameterFilter
{
    public class IsolateExternal : IExternalEventHandler
    {
        // Same data contract as ParamExternal — set these from the UI before Raise()
        public ElementId? ParameterElementId { get; set; }
        public ElementId? CategoryId { get; set; }
        public string? FilterValue { get; set; }
        public string? RuleOperator { get; set; }

        public void Execute(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;

            if (uidoc == null)
            {
                TaskDialog.Show("Error", "No active document.");
                return;
            }

            var doc = uidoc.Document;
            var view = uidoc.ActiveView;

            // ── Validate ────────────────────────────────────────────────────
            if (ParameterElementId == null)
            { TaskDialog.Show("Validation", "Please select a Parameter."); return; }

            if (CategoryId == null)
            { TaskDialog.Show("Validation", "Please select a Category."); return; }

            if (string.IsNullOrWhiteSpace(RuleOperator))
            { TaskDialog.Show("Validation", "Please select an Operator."); return; }

            if (string.IsNullOrWhiteSpace(FilterValue))
            { TaskDialog.Show("Validation", "Please enter a filter value."); return; }

            // ── Build filter ─────────────────────────────────────────────────
            var paramFilter = BuildFilter(ParameterElementId, RuleOperator!, FilterValue!);
            if (paramFilter == null)
            {
                TaskDialog.Show("Error", $"Could not build filter for operator '{RuleOperator}'.");
                return;
            }

            try
            {
                // ── Collect matching element IDs ─────────────────────────────
                //    Only elements VISIBLE in the current view can be isolated.
                //    Passing the view into the collector restricts results to
                //    what's already visible, avoiding the "element not in view"
                //    exception from IsolateElementsTemporary.
                var matchingIds = new FilteredElementCollector(doc, view.Id)
                    .OfCategoryId(CategoryId)
                    .WhereElementIsNotElementType()
                    .WherePasses(paramFilter)
                    .ToElementIds()
                    .ToList();

                if (matchingIds.Count == 0)
                {
                    TaskDialog.Show("Isolate", "No visible elements matched the filter.");
                    return;
                }

                // ── Isolate inside a transaction ──────────────────────────────
                using var tx = new Transaction(doc, "Isolate by Parameter");
                tx.Start();

                //  IsolateElementsTemporary:
                //  - Works only on the active view
                //  - Revit will auto-clear it when the user ends isolation
                //    (the blue "Isolation active" bar appears at the top of the view)
                //  - Cannot be used when the view already has a permanent override;
                //    in that case call view.DisableTemporaryViewMode first.
                if (view.IsTemporaryHideIsolateActive())
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);

                view.IsolateElementsTemporary(matchingIds);

                tx.Commit();

                TaskDialog.Show(
                    "Isolate",
                    $"{matchingIds.Count} element(s) isolated in the active view.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
        }

        // ── Shared filter builder ────────────────────────────────────────────
        //    Identical to the one in ParamExternal. Consider extracting both
        //    into a static FilterRuleBuilder helper class to avoid duplication.
        private static ElementParameterFilter? BuildFilter(
            ElementId parameterId,
            string ruleOperator,
            string value)
        {
            var provider = new ParameterValueProvider(parameterId);

            FilterRule rule = ruleOperator switch
            {
                "Equals" => new FilterStringRule(provider, new FilterStringEquals(), value),
                "Contains" => new FilterStringRule(provider, new FilterStringContains(), value),
                "Begins With" => new FilterStringRule(provider, new FilterStringBeginsWith(), value),
                "Ends With" => new FilterStringRule(provider, new FilterStringEndsWith(), value),

                "Greater Than" => double.TryParse(value, out var gt)
                    ? new FilterDoubleRule(provider, new FilterNumericGreater(), gt, 1e-6)
                    : null!,

                "Less Than" => double.TryParse(value, out var lt)
                    ? new FilterDoubleRule(provider, new FilterNumericLess(), lt, 1e-6)
                    : null!,

                _ => null!
            };

            return rule is null ? null : new ElementParameterFilter(rule);
        }

        public string GetName() => "Isolate Elements by Parameter";
    }
}