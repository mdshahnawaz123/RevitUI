using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitUI.ExternalCommand.ParameterFilter
{
    public class ParamExternal : IExternalEventHandler
    {
        // Use correct types — strings/IDs passed from the UI
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

            // Validate inputs before starting the transaction
            if (ParameterElementId == null)
            {
                TaskDialog.Show("Validation", "Please select a Parameter.");
                return;
            }

            if (CategoryId == null)
            {
                TaskDialog.Show("Validation", "Please select a Category.");
                return;
            }

            if (string.IsNullOrWhiteSpace(RuleOperator))
            {
                TaskDialog.Show("Validation", "Please select an Operator.");
                return;
            }

            if (string.IsNullOrWhiteSpace(FilterValue))
            {
                TaskDialog.Show("Validation", "Please enter a filter value.");
                return;
            }

            try
            {
                // Build the filter rule based on selected operator
                var provider = new ParameterValueProvider(ParameterElementId);
                ElementParameterFilter paramFilter = BuildFilter(provider, RuleOperator, FilterValue);

                if (paramFilter == null)
                {
                    TaskDialog.Show("Error", $"Unsupported operator: {RuleOperator}");
                    return;
                }

                // Collect matching elements from the host model
                var matchingIds = new List<ElementId>();

                var hostElements = new FilteredElementCollector(doc)
                    .OfCategoryId(CategoryId)
                    .WhereElementIsNotElementType()
                    .WherePasses(paramFilter)
                    .ToElementIds()
                    .ToList();

                matchingIds.AddRange(hostElements);

                if (matchingIds.Count == 0)
                {
                    TaskDialog.Show("Result", "No elements matched the filter.");
                    return;
                }

                // Show all matching elements in one call
                uidoc.ShowElements(matchingIds);
                TaskDialog.Show("Result", $"{matchingIds.Count} element(s) found and shown.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
        }

        private ElementParameterFilter BuildFilter(
            ParameterValueProvider provider,
            string ruleOperator,
            string value)
        {
            FilterRule rule;

            switch (ruleOperator)
            {
                case "Equals":
                    rule = new FilterStringRule(provider, new FilterStringEquals(), value);
                    break;
                case "Contains":
                    rule = new FilterStringRule(provider, new FilterStringContains(), value);
                    break;
                case "Begins With":
                    rule = new FilterStringRule(provider, new FilterStringBeginsWith(), value);
                    break;
                case "Ends With":
                    rule = new FilterStringRule(provider, new FilterStringEndsWith(), value);
                    break;
                case "Greater Than":
                    // Numeric: parse the value as double
                    if (double.TryParse(value, out double gtVal))
                        rule = new FilterDoubleRule(provider, new FilterNumericGreater(), gtVal, 1e-6);
                    else
                    {
                        TaskDialog.Show("Error", "Greater Than requires a numeric value.");
                        return null;
                    }
                    break;
                case "Less Than":
                    if (double.TryParse(value, out double ltVal))
                        rule = new FilterDoubleRule(provider, new FilterNumericLess(), ltVal, 1e-6);
                    else
                    {
                        TaskDialog.Show("Error", "Less Than requires a numeric value.");
                        return null;
                    }
                    break;
                default:
                    return null;
            }

            return new ElementParameterFilter(rule);
        }

        public string GetName() => "Parameter Filter Handler";
    }
}