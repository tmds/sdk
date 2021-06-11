// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TemplateEngine.Abstractions.TemplateFiltering;
using Microsoft.TemplateEngine.Utils;

namespace Microsoft.TemplateEngine.Cli.TemplateResolution
{
    /// <summary>
    /// Class represents the template resolution result for template instantiation based on command input.
    /// Before template is resolved all installed templates are grouped by template group ID. Templates in single group:<br/>
    /// - should have different template identity <br/>
    /// - same short name (however different short names are also supported) <br/>
    /// - the templates may have different languages and types <br/>
    /// - the templates should have different precedence value in case same language is used <br/>
    /// - the templates in the group may have different parameters and different choices for parameter symbols defined<br/>
    /// Template resolution is done in several steps:<br/>
    /// 1) the list of matched templates is defined based on command input (template name and filters used)<br/>
    /// 2) the unambiguous template group to use is defined. In case of ambiguity, if the user didn't specify language to use, the groups with the templates defined in default language are preferred. In case unambiguous template group cannot be resolved, the template to instantiate cannot be resolved as well.<br/>
    /// 3) the template to invoke inside the group is defined:<br/>
    /// -- the template that matches template specific options in command input is preferred<br/>
    /// -- in case there are multiple templates, the one with highest precedence is selected<br/>
    /// -- in case there are multiple templates with same precedence and user didn't specify the language to use, the default language template is preferred.<br/>
    /// -- Note that the template will not be resolved in case the value specified for choice parameter is not exact and there is an ambiguity between template to select:<br/>
    /// --- in case at least one template in the group has more than 1 choice value which starts with specified value in the command<br/>
    /// --- in case at least two templates in the group have 1 choice value which starts with specified value in the command.<br/>
    /// </summary>
    internal class TemplateResolutionResult
    {
        private readonly IReadOnlyCollection<ITemplateMatchInfo> _coreMatchedTemplates;

        private readonly bool _hasUserInputLanguage;

        private Status _singularInvokableMatchStatus = Status.NotEvaluated;

        private IReadOnlyCollection<TemplateGroup>? _templateGroups;

        private ITemplateMatchInfo? _templateToInvoke;

        private TemplateGroup? _unambiguousTemplateGroup;

        private UnambiguousTemplateGroupStatus _unambigiousTemplateGroupStatus = UnambiguousTemplateGroupStatus.NotEvaluated;

        internal TemplateResolutionResult(string userInputLanguage, IReadOnlyCollection<ITemplateMatchInfo> coreMatchedTemplates)
        {
            _hasUserInputLanguage = !string.IsNullOrEmpty(userInputLanguage);
            _coreMatchedTemplates = coreMatchedTemplates;
        }

        /// <summary>
        /// Enum defines possible statuses for resolving template to invoke.<br />
        /// </summary>
        internal enum Status
        {
            /// <summary>
            /// the status is not evaluated yet.
            /// </summary>
            NotEvaluated,

            /// <summary>
            /// no matched template groups were resolved.
            /// </summary>
            NoMatch,

            /// <summary>
            /// single template group and single template to use in the group is resolved.
            /// </summary>
            SingleMatch,

            /// <summary>
            /// multiple template groups were resolved; not possible to determine the group to use.
            /// </summary>
            AmbiguousTemplateGroupChoice,

            /// <summary>
            /// single template group was resolved, but there is an ambiguous choice for template inside the group and the templates are of same language. Usually means that the installed templates are conflicting and the conflict should be resolved by uninistalling some of templates.
            /// </summary>
            AmbiguousTemplateChoice,

            /// <summary>
            /// single template group was resolved, but there is an ambiguous choice for template inside the group with templates having different languages and the language was not selected by user and no default language match.
            /// </summary>
            AmbiguousLanguageChoice,

            /// <summary>
            /// single template group was resolved, but parameters or choice parameter values provided are invalid for all templates in the group.
            /// </summary>
            InvalidParameter
        }

        /// <summary>
        /// Enum defines possible statuses for unambiguous template group resolution.
        /// </summary>
        internal enum UnambiguousTemplateGroupStatus
        {
            /// <summary>
            /// the status is not evaluated yet.
            /// </summary>
            NotEvaluated,

            /// <summary>
            /// no matched template groups were resolved.
            /// </summary>
            NoMatch,

            /// <summary>
            /// single template group is resolved.
            /// </summary>
            SingleMatch,

            /// <summary>
            /// multiple template groups were resolved; not possible to determining the group to use.
            /// </summary>
            Ambiguous
        }

        /// <summary>
        /// Returns status of template resolution. <br />
        /// </summary>
        internal Status ResolutionStatus
        {
            get
            {
                if (_singularInvokableMatchStatus == Status.NotEvaluated)
                {
                    EvaluateTemplateToInvoke();
                }
                return _singularInvokableMatchStatus;
            }
        }

        /// <summary>
        /// Returns the template to invoke or <c>null</c> if the template to invoke cannot be determined.
        /// Has value only when <see cref="Status" /> is <see cref="Status.SingleMatch"/>.
        /// </summary>
        internal ITemplateMatchInfo? TemplateToInvoke
        {
            get
            {
                if (_singularInvokableMatchStatus == Status.NotEvaluated)
                {
                    EvaluateTemplateToInvoke();
                }
                return _templateToInvoke;
            }
        }

        /// <summary>
        /// Returns template groups that matches command input (template specific options are not considered in the match).
        /// </summary>
        internal IReadOnlyCollection<TemplateGroup> TemplateGroups
        {
            get
            {
                if (_templateGroups == null)
                {
                    _templateGroups = _coreMatchedTemplates
                        .GroupBy(x => x.Info.GroupIdentity, x => !string.IsNullOrEmpty(x.Info.GroupIdentity), StringComparer.OrdinalIgnoreCase)
                        .Select(group => new TemplateGroup(group.ToList()))
                        .ToList();
                }
                return _templateGroups;
            }
        }

        /// <summary>
        /// Returns status of unambiguous template group resolution.
        /// </summary>
        internal UnambiguousTemplateGroupStatus GroupResolutionStatus
        {
            get
            {
                if (_unambigiousTemplateGroupStatus == UnambiguousTemplateGroupStatus.NotEvaluated)
                {
                    EvaluateUnambiguousTemplateGroup();
                }
                return _unambigiousTemplateGroupStatus;
            }
        }

        /// <summary>
        /// Returns unambiguous template group resolved; <c>null</c> if group cannot be resolved based on command input
        /// Has value only when <see cref="GroupResolutionStatus" /> is <see cref="UnambiguousTemplateGroupStatus.SingleMatch"/>.
        /// </summary>
        internal TemplateGroup? UnambiguousTemplateGroup
        {
            get
            {
                if (_unambigiousTemplateGroupStatus == UnambiguousTemplateGroupStatus.NotEvaluated)
                {
                    EvaluateUnambiguousTemplateGroup();
                }
                return _unambiguousTemplateGroup;
            }
        }

        internal IEnumerable<ITemplateMatchInfo> TemplatesForDetailedHelp
        {
            get
            {
                if (UnambiguousTemplateGroup == null || !UnambiguousTemplateGroup.InvokableTemplates.Any())
                {
                    return Array.Empty<ITemplateMatchInfo>();
                }

                if (_hasUserInputLanguage)
                {
                    return UnambiguousTemplateGroup.InvokableTemplates.Where(t => t.HasLanguageMatch());
                }
                else if (UnambiguousTemplateGroup.InvokableTemplates.Any(t => t.HasDefaultLanguageMatch()))
                {
                    return UnambiguousTemplateGroup.InvokableTemplates.Where(t => t.HasDefaultLanguageMatch());
                }
                else
                {
                    HashSet<string> languagesFound = new HashSet<string>();
                    foreach (ITemplateMatchInfo template in UnambiguousTemplateGroup.InvokableTemplates)
                    {
                        string? language = template.Info.GetLanguage();
                        if (!string.IsNullOrEmpty(language))
                        {
                            languagesFound.Add(language);
                        }

                        if (languagesFound.Count > 1)
                        {
                            //not possible to identify language to show template for
                            return Array.Empty<ITemplateMatchInfo>();
                        }
                    }
                    return UnambiguousTemplateGroup.InvokableTemplates;
                }
            }
        }

        private void EvaluateTemplateToInvoke()
        {
            EvaluateUnambiguousTemplateGroup();
            switch (GroupResolutionStatus)
            {
                case UnambiguousTemplateGroupStatus.NotEvaluated:
                    throw new ArgumentException($"{nameof(GroupResolutionStatus)} should not be {nameof(UnambiguousTemplateGroupStatus.NotEvaluated)} after running {nameof(EvaluateUnambiguousTemplateGroup)}");
                case UnambiguousTemplateGroupStatus.NoMatch:
                    _singularInvokableMatchStatus = Status.NoMatch;
                    return;
                case UnambiguousTemplateGroupStatus.Ambiguous:
                    _singularInvokableMatchStatus = Status.AmbiguousTemplateGroupChoice;
                    return;
                case UnambiguousTemplateGroupStatus.SingleMatch:
                    if (UnambiguousTemplateGroup == null)
                    {
                        throw new ArgumentException($"{nameof(UnambiguousTemplateGroup)} should not be null if running {nameof(GroupResolutionStatus)} is {nameof(UnambiguousTemplateGroupStatus.SingleMatch)}");
                    }
                    //valid state to proceed
                    break;
                default:
                    throw new ArgumentException($"Unexpected value of {nameof(UnambiguousTemplateGroup)}: {GroupResolutionStatus}.");
            }

            //if no templates are invokable there is a problem with parameter name or value - cannot resolve template to instantiate
            if (!UnambiguousTemplateGroup.InvokableTemplates.Any())
            {
                _singularInvokableMatchStatus = Status.InvalidParameter;
                return;
            }

            if (UnambiguousTemplateGroup.InvokableTemplates.Count() == 1)
            {
                _templateToInvoke = UnambiguousTemplateGroup.InvokableTemplates.Single();
                _singularInvokableMatchStatus = Status.SingleMatch;
                return;
            }

            if (UnambiguousTemplateGroup.TryGetHighestPrecedenceInvokableTemplate(out _templateToInvoke, !_hasUserInputLanguage))
            {
                _singularInvokableMatchStatus = Status.SingleMatch;
                return;
            }

            IEnumerable<ITemplateMatchInfo> highestPrecedenceTemplates = UnambiguousTemplateGroup.GetHighestPrecedenceInvokableTemplates(!_hasUserInputLanguage);
            IEnumerable<string?> templateLanguages = highestPrecedenceTemplates.Select(t => t.Info.GetLanguage()).Distinct(StringComparer.OrdinalIgnoreCase);

            if (templateLanguages.Count() > 1)
            {
                _singularInvokableMatchStatus = Status.AmbiguousLanguageChoice;
                return;
            }
            _singularInvokableMatchStatus = Status.AmbiguousTemplateChoice;
        }

        private void EvaluateUnambiguousTemplateGroup()
        {
            if (TemplateGroups.Count == 0)
            {
                _unambigiousTemplateGroupStatus = UnambiguousTemplateGroupStatus.NoMatch;
                return;
            }
            if (TemplateGroups.Count == 1)
            {
                _unambiguousTemplateGroup = TemplateGroups.Single();
                _unambigiousTemplateGroupStatus = UnambiguousTemplateGroupStatus.SingleMatch;
                return;
            }
            else if (!_hasUserInputLanguage)
            {
                // only consider default language match dispositions if the user did not specify a language.
                try
                {
                    _unambiguousTemplateGroup = TemplateGroups.SingleOrDefault(group => group.Templates.Any(x => x.HasDefaultLanguageMatch()));
                }
                catch (InvalidOperationException)
                {
                    _unambigiousTemplateGroupStatus = UnambiguousTemplateGroupStatus.Ambiguous;
                    return;
                }
                if (_unambiguousTemplateGroup != null)
                {
                    _unambigiousTemplateGroupStatus = UnambiguousTemplateGroupStatus.SingleMatch;
                    return;
                }
            }
            _unambigiousTemplateGroupStatus = UnambiguousTemplateGroupStatus.Ambiguous;
        }
    }
}
