// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Data.Entity.Internal;

namespace Microsoft.Data.Entity.Metadata.Internal
{
    public class InternalRelationshipBuilder : InternalMetadataItemBuilder<ForeignKey>
    {
        private ConfigurationSource? _foreignKeyPropertiesConfigurationSource;
        private ConfigurationSource? _principalKeyConfigurationSource;
        private ConfigurationSource? _isUniqueConfigurationSource;
        private ConfigurationSource? _isRequiredConfigurationSource;
        private ConfigurationSource? _principalEndConfigurationSource;

        public InternalRelationshipBuilder(
            [NotNull] ForeignKey foreignKey,
            [NotNull] InternalModelBuilder modelBuilder,
            ConfigurationSource? initialConfigurationSource)
            : base(foreignKey, modelBuilder)
        {
            _foreignKeyPropertiesConfigurationSource = initialConfigurationSource;
            _principalKeyConfigurationSource = initialConfigurationSource;
            _isUniqueConfigurationSource = initialConfigurationSource;
            _isRequiredConfigurationSource = initialConfigurationSource;
            _principalEndConfigurationSource = initialConfigurationSource;
        }

        public virtual InternalRelationshipBuilder NavigationToPrincipal(
            [CanBeNull] string navigationToPrincipalName,
            ConfigurationSource configurationSource,
            bool? strictPreferExisting = null)
        {
            var dependentEntityType = ModelBuilder.Entity(Metadata.DeclaringEntityType.Name, configurationSource);

            if (strictPreferExisting.HasValue)
            {
                var navigationToPrincipal = string.IsNullOrEmpty(navigationToPrincipalName)
                    ? null
                    : Metadata.DeclaringEntityType.FindDeclaredNavigation(navigationToPrincipalName);

                if (navigationToPrincipal != null
                    && navigationToPrincipal.IsCompatible(
                        Metadata.PrincipalEntityType,
                        Metadata.DeclaringEntityType,
                        strictPreferExisting.Value ? (bool?)true : null,
                        Metadata.IsUnique))
                {
                    var navigationToDependentName = Metadata.PrincipalToDependent?.Name ?? "";

                    if (Metadata == navigationToPrincipal.ForeignKey
                        || dependentEntityType.RemoveRelationship(Metadata, configurationSource).HasValue)
                    {
                        return dependentEntityType.Relationship(
                            navigationToPrincipal,
                            configurationSource,
                            navigationToDependentName);
                    }
                }
            }

            var hasChanged = navigationToPrincipalName != null &&
                             Metadata.DependentToPrincipal?.Name != navigationToPrincipalName;

            return dependentEntityType
                .Navigation(navigationToPrincipalName, Metadata, pointsToPrincipal: true, configurationSource: configurationSource) != null
                ? hasChanged ? ReplaceForeignKey(configurationSource) : this
                : null;
        }

        public virtual InternalRelationshipBuilder NavigationToDependent(
            [CanBeNull] string navigationToDependentName,
            ConfigurationSource configurationSource,
            bool? strictPreferExisting = null)
        {
            var principalEntityType = ModelBuilder.Entity(Metadata.PrincipalEntityType.Name, configurationSource);

            if (strictPreferExisting.HasValue)
            {
                var navigationToDependent = navigationToDependentName == null
                    ? null
                    : Metadata.PrincipalEntityType.FindDeclaredNavigation(navigationToDependentName);

                if (navigationToDependent != null
                    && navigationToDependent.IsCompatible(
                        Metadata.PrincipalEntityType,
                        Metadata.DeclaringEntityType,
                        strictPreferExisting.Value ? (bool?)false : null,
                        Metadata.IsUnique))
                {
                    var navigationToPrincipalName = Metadata.DependentToPrincipal?.Name ?? "";

                    if (Metadata == navigationToDependent.ForeignKey
                        || principalEntityType.RemoveRelationship(Metadata, configurationSource).HasValue)
                    {
                        return principalEntityType.Relationship(
                            navigationToDependent,
                            configurationSource,
                            navigationToPrincipalName);
                    }
                }
            }

            var hasChanged = navigationToDependentName != null &&
                             Metadata.PrincipalToDependent?.Name != navigationToDependentName;

            return principalEntityType
                .Navigation(navigationToDependentName, Metadata, pointsToPrincipal: false, configurationSource: configurationSource) != null
                ? hasChanged ? ReplaceForeignKey(configurationSource) : this
                : null;
        }

        public virtual InternalRelationshipBuilder Required(bool? isRequired, ConfigurationSource configurationSource)
        {
            if (((IForeignKey)Metadata).IsRequired == isRequired)
            {
                Metadata.IsRequired = isRequired;
                _isRequiredConfigurationSource = configurationSource.Max(_isRequiredConfigurationSource);
                return this;
            }

            if (_isRequiredConfigurationSource != null
                && !configurationSource.Overrides(_isRequiredConfigurationSource.Value))
            {
                return null;
            }

            if (isRequired.HasValue
                && _foreignKeyPropertiesConfigurationSource.HasValue
                && _foreignKeyPropertiesConfigurationSource.Value.Overrides(configurationSource)
                && !ModelBuilder.Entity(Metadata.DeclaringEntityType.Name, configurationSource)
                    .CanSetRequired(Metadata, isRequired.Value, configurationSource))
            {
                return null;
            }

            _isRequiredConfigurationSource = configurationSource.Max(_isRequiredConfigurationSource);
            return ReplaceForeignKey(configurationSource, isRequired: isRequired);
        }

        public virtual InternalRelationshipBuilder Unique(bool? isUnique, ConfigurationSource configurationSource)
        {
            if (((IForeignKey)Metadata).IsUnique == isUnique)
            {
                Metadata.IsUnique = isUnique;
                _isUniqueConfigurationSource = configurationSource.Max(_isUniqueConfigurationSource);
                return this;
            }

            if (Metadata.PrincipalToDependent != null)
            {
                // TODO: throw for explicit
                return null;
            }

            if (_isUniqueConfigurationSource != null
                && !configurationSource.Overrides(_isUniqueConfigurationSource.Value))
            {
                return null;
            }

            _isUniqueConfigurationSource = configurationSource.Max(_isUniqueConfigurationSource);
            return ReplaceForeignKey(configurationSource, isUnique: isUnique);
        }

        public virtual InternalRelationshipBuilder Invert(ConfigurationSource configurationSource)
        {
            if (!((IForeignKey)Metadata).IsUnique)
            {
                return null;
            }

            if ((_foreignKeyPropertiesConfigurationSource != null && _foreignKeyPropertiesConfigurationSource.Value.Overrides(configurationSource))
                || (_principalKeyConfigurationSource != null && _principalKeyConfigurationSource.Value.Overrides(configurationSource))
                || (_principalEndConfigurationSource != null && !configurationSource.Overrides(_principalEndConfigurationSource.Value)))
            {
                if (configurationSource == ConfigurationSource.Explicit)
                {
                    throw new InvalidOperationException(Strings.RelationshipCannotBeInverted);
                }
                return null;
            }

            _principalEndConfigurationSource = configurationSource.Max(_principalEndConfigurationSource);
            _foreignKeyPropertiesConfigurationSource = null;
            _principalKeyConfigurationSource = null;

            return ReplaceForeignKey(
                Metadata.DeclaringEntityType,
                Metadata.PrincipalEntityType,
                Metadata.PrincipalToDependent?.Name,
                Metadata.DependentToPrincipal?.Name,
                null,
                null,
                ((IForeignKey)Metadata).IsUnique,
                _isRequiredConfigurationSource.HasValue ? ((IForeignKey)Metadata).IsRequired : (bool?)null,
                configurationSource);
        }

        public virtual InternalRelationshipBuilder ForeignKey([CanBeNull] IReadOnlyList<PropertyInfo> properties,
            ConfigurationSource configurationSource)
            => ForeignKey(
                ModelBuilder.Entity(Metadata.DeclaringEntityType.Name, configurationSource)
                    .GetOrCreateProperties(properties, configurationSource),
                configurationSource);

        public virtual InternalRelationshipBuilder ForeignKey([CanBeNull] IReadOnlyList<string> propertyNames,
            ConfigurationSource configurationSource)
            => ForeignKey(
                ModelBuilder.Entity(Metadata.DeclaringEntityType.Name, configurationSource)
                    .GetOrCreateProperties(propertyNames, configurationSource),
                configurationSource);

        public virtual InternalRelationshipBuilder ForeignKey([CanBeNull] IReadOnlyList<Property> properties,
            ConfigurationSource configurationSource)
        {
            if (properties != null
                && Metadata.Properties.SequenceEqual(properties))
            {
                _foreignKeyPropertiesConfigurationSource = configurationSource.Max(_foreignKeyPropertiesConfigurationSource);

                ModelBuilder.Entity(Metadata.DeclaringEntityType.Name, configurationSource)
                    .GetOrCreateProperties(properties.Select(p => p.Name), configurationSource);
                return this;
            }

            if (_foreignKeyPropertiesConfigurationSource != null
                && !configurationSource.Overrides(_foreignKeyPropertiesConfigurationSource.Value))
            {
                return null;
            }

            var originalForeignKeyPropertiesConfigurationSource = _foreignKeyPropertiesConfigurationSource;
            if (properties == null
                || properties.Count == 0)
            {
                properties = null;
                _foreignKeyPropertiesConfigurationSource = null;
            }
            else
            {
                _foreignKeyPropertiesConfigurationSource = configurationSource.Max(_foreignKeyPropertiesConfigurationSource);
            }

            var newForeignKey = ReplaceForeignKey(configurationSource, dependentProperties: properties);
            if (newForeignKey == null)
            {
                _foreignKeyPropertiesConfigurationSource = originalForeignKeyPropertiesConfigurationSource;
            }

            return newForeignKey;
        }

        public virtual bool CanSetForeignKey([NotNull] IReadOnlyList<Property> properties, ConfigurationSource configurationSource)
        {
            if (_foreignKeyPropertiesConfigurationSource.HasValue
                && !configurationSource.Overrides(_foreignKeyPropertiesConfigurationSource.Value))
            {
                return false;
            }

            if (!_isRequiredConfigurationSource.HasValue
                || configurationSource.Overrides(_isRequiredConfigurationSource.Value)
                || properties.Count == 0)
            {
                return true;
            }

            var isRequired = ((IForeignKey)Metadata).IsRequired;
            if (!isRequired
                && !properties.Any(p => p.ClrType.IsNullableType()))
            {
                return false;
            }

            return CanSetRequired(properties, isRequired, configurationSource);
        }

        private bool CanSetRequired([NotNull] IReadOnlyList<Property> properties, bool isRequired, ConfigurationSource configurationSource)
        {
            var entityBuilder = ModelBuilder.Entity(properties[0].DeclaringEntityType.Name, ConfigurationSource.Convention);
            return entityBuilder.CanSetRequired(properties, isRequired, configurationSource);
        }

        public virtual InternalRelationshipBuilder ForeignKey(
            [NotNull] Type specifiedDependentType,
            [CanBeNull] IReadOnlyList<PropertyInfo> properties,
            ConfigurationSource configurationSource)
            => ForeignInvertIfNeeded(ResolveType(specifiedDependentType), configurationSource)
                .ForeignKey(properties, configurationSource);

        public virtual InternalRelationshipBuilder ForeignKey(
            [NotNull] Type specifiedDependentType,
            [CanBeNull] IReadOnlyList<string> propertyNames,
            ConfigurationSource configurationSource)
            => ForeignInvertIfNeeded(ResolveType(specifiedDependentType), configurationSource)
                .ForeignKey(propertyNames, configurationSource);

        public virtual InternalRelationshipBuilder ForeignKey(
            [NotNull] string specifiedDependentTypeName,
            [CanBeNull] IReadOnlyList<string> propertyNames,
            ConfigurationSource configurationSource)
            => ForeignInvertIfNeeded(ResolveType(specifiedDependentTypeName), configurationSource)
                .ForeignKey(propertyNames, configurationSource);

        private InternalRelationshipBuilder ForeignInvertIfNeeded(EntityType entityType, ConfigurationSource configurationSource)
        {
            _principalEndConfigurationSource = configurationSource.Max(_principalEndConfigurationSource);
            return entityType == Metadata.DeclaringEntityType
                ? this
                : Invert(configurationSource);
        }

        public virtual InternalRelationshipBuilder PrincipalKey([CanBeNull] IReadOnlyList<PropertyInfo> properties,
            ConfigurationSource configurationSource)
            => PrincipalKey(
                ModelBuilder.Entity(Metadata.PrincipalEntityType.Name, configurationSource)
                    .GetOrCreateProperties(properties, configurationSource),
                configurationSource);

        public virtual InternalRelationshipBuilder PrincipalKey([CanBeNull] IReadOnlyList<string> propertyNames,
            ConfigurationSource configurationSource)
            => PrincipalKey(
                ModelBuilder.Entity(Metadata.PrincipalEntityType.Name, configurationSource)
                    .GetOrCreateProperties(propertyNames, configurationSource),
                configurationSource);

        public virtual InternalRelationshipBuilder PrincipalKey([CanBeNull] IReadOnlyList<Property> properties,
            ConfigurationSource configurationSource)
        {
            if (properties != null
                && Metadata.PrincipalKey.Properties.SequenceEqual(properties))
            {
                var principalEntityTypeBuilder = ModelBuilder.Entity(Metadata.PrincipalEntityType.Name, configurationSource);
                principalEntityTypeBuilder.Key(properties, configurationSource);
                _principalKeyConfigurationSource = configurationSource.Max(_principalKeyConfigurationSource);
                return this;
            }

            if (_principalKeyConfigurationSource != null
                && !configurationSource.Overrides(_principalKeyConfigurationSource.Value))
            {
                return null;
            }

            if (properties == null
                || properties.Count == 0)
            {
                properties = null;
                _principalKeyConfigurationSource = null;
            }
            else
            {
                _principalKeyConfigurationSource = configurationSource.Max(_principalKeyConfigurationSource);
            }

            return ReplaceForeignKey(configurationSource, principalProperties: properties);
        }

        public virtual InternalRelationshipBuilder UpdatePrincipalKey([NotNull] IReadOnlyList<Property> properties,
            ConfigurationSource configurationSource)
        {
            if (Metadata.PrincipalKey.Properties.SequenceEqual(properties))
            {
                return this;
            }

            if (_principalKeyConfigurationSource != null
                && _principalKeyConfigurationSource.Value.Overrides(configurationSource))
            {
                return null;
            }

            if (_foreignKeyPropertiesConfigurationSource.HasValue
                && _foreignKeyPropertiesConfigurationSource.Value.Overrides(configurationSource)
                && !Property.AreCompatible(properties, Metadata.Properties))
            {
                return null;
            }

            return ReplaceForeignKey(configurationSource, principalProperties: properties);
        }

        public virtual InternalRelationshipBuilder PrincipalKey(
            [NotNull] Type specifiedPrincipalType,
            [CanBeNull] IReadOnlyList<PropertyInfo> properties,
            ConfigurationSource configurationSource)
            => ReferenceInvertIfNeeded(ResolveType(specifiedPrincipalType), configurationSource)
                .PrincipalKey(properties, configurationSource);

        public virtual InternalRelationshipBuilder PrincipalKey(
            [NotNull] Type specifiedPrincipalType,
            [CanBeNull] IReadOnlyList<string> propertyNames,
            ConfigurationSource configurationSource)
            => ReferenceInvertIfNeeded(ResolveType(specifiedPrincipalType), configurationSource)
                .PrincipalKey(propertyNames, configurationSource);

        public virtual InternalRelationshipBuilder PrincipalKey(
            [NotNull] string specifiedPrincipalTypeName,
            [CanBeNull] IReadOnlyList<string> propertyNames,
            ConfigurationSource configurationSource)
            => ReferenceInvertIfNeeded(ResolveType(specifiedPrincipalTypeName), configurationSource)
                .PrincipalKey(propertyNames, configurationSource);

        private InternalRelationshipBuilder ReferenceInvertIfNeeded(EntityType entityType, ConfigurationSource configurationSource)
        {
            _principalEndConfigurationSource = configurationSource.Max(_principalEndConfigurationSource);
            return entityType == Metadata.PrincipalEntityType
                ? this
                : Invert(configurationSource);
        }

        private InternalRelationshipBuilder ReplaceForeignKey(ConfigurationSource configurationSource,
            IReadOnlyList<Property> dependentProperties = null,
            IReadOnlyList<Property> principalProperties = null,
            bool? isUnique = null,
            bool? isRequired = null)
        {
            dependentProperties = dependentProperties ??
                                  (_foreignKeyPropertiesConfigurationSource.HasValue
                                   && _foreignKeyPropertiesConfigurationSource.Value.Overrides(configurationSource)
                                      ? Metadata.Properties
                                      : null);

            principalProperties = principalProperties ??
                                  (_principalKeyConfigurationSource.HasValue
                                   && _principalKeyConfigurationSource.Value.Overrides(configurationSource)
                                      ? Metadata.PrincipalKey.Properties
                                      : null);

            var navigationToDependentName = Metadata.PrincipalToDependent?.Name;

            isUnique = isUnique ??
                       (navigationToDependentName != null
                           ? ((IForeignKey)Metadata).IsUnique
                           : (bool?)null);

            isUnique = isUnique ??
                       (_isUniqueConfigurationSource.HasValue
                        && _isUniqueConfigurationSource.Value.Overrides(configurationSource)
                           ? ((IForeignKey)Metadata).IsUnique
                           : (bool?)null);

            isRequired = isRequired ??
                         (_isRequiredConfigurationSource.HasValue
                          && _isRequiredConfigurationSource.Value.Overrides(configurationSource)
                             ? ((IForeignKey)Metadata).IsRequired
                             : (bool?)null);

            if (dependentProperties != null
                && isRequired.HasValue
                && !CanSetRequired(dependentProperties, isRequired.Value, configurationSource))
            {
                return null;
            }

            return ReplaceForeignKey(
                Metadata.PrincipalEntityType,
                Metadata.DeclaringEntityType,
                Metadata.DependentToPrincipal?.Name,
                navigationToDependentName,
                dependentProperties,
                principalProperties,
                isUnique,
                isRequired,
                configurationSource);
        }

        private InternalRelationshipBuilder ReplaceForeignKey(
            [NotNull] EntityType principalType,
            [NotNull] EntityType dependentType,
            [CanBeNull] string navigationToPrincipalName,
            [CanBeNull] string navigationToDependentName,
            [CanBeNull] IReadOnlyList<Property> foreignKeyProperties,
            [CanBeNull] IReadOnlyList<Property> principalProperties,
            bool? isUnique,
            bool? isRequired,
            ConfigurationSource configurationSource)
        {
            var replacedConfigurationSource = ModelBuilder
                .Entity(Metadata.DeclaringEntityType.Name, configurationSource)
                .RemoveRelationship(Metadata, ConfigurationSource.Explicit);

            return !replacedConfigurationSource.HasValue
                ? null
                : AddRelationship(
                    principalType,
                    dependentType,
                    navigationToPrincipalName,
                    navigationToDependentName,
                    foreignKeyProperties,
                    principalProperties,
                    isUnique,
                    isRequired,
                    configurationSource,
                    replacedConfigurationSource);
        }

        private InternalRelationshipBuilder AddRelationship(
            EntityType principalType,
            EntityType dependentType,
            string navigationToPrincipalName,
            string navigationToDependentName,
            IReadOnlyList<Property> foreignKeyProperties,
            IReadOnlyList<Property> principalProperties,
            bool? isUnique,
            bool? isRequired,
            ConfigurationSource configurationSource,
            ConfigurationSource? replacedConfigurationSource)
        {
            var principalEntityTypeBuilder = ModelBuilder.Entity(principalType.Name, configurationSource);
            var dependentEntityTypeBuilder = ModelBuilder.Entity(dependentType.Name, configurationSource);

            return dependentEntityTypeBuilder.Relationship(
                principalEntityTypeBuilder,
                dependentEntityTypeBuilder,
                navigationToPrincipalName,
                navigationToDependentName,
                foreignKeyProperties,
                principalProperties,
                configurationSource,
                isUnique,
                isRequired,
                b => dependentEntityTypeBuilder
                    .Relationship(b.Metadata, existingForeignKey: true, configurationSource: replacedConfigurationSource.Value)
                    .MergeConfigurationSourceWith(this, configurationSource));
        }

        public virtual InternalRelationshipBuilder Attach(ConfigurationSource configurationSource)
        {
            if (Metadata.DeclaringEntityType.GetForeignKeys().Contains(Metadata))
            {
                return ModelBuilder.Entity(Metadata.DeclaringEntityType.Name, configurationSource)
                    .Relationship(Metadata, existingForeignKey: true, configurationSource: configurationSource);
            }

            var dependentPropertiesExist = true;
            foreach (var dependentProperty in Metadata.Properties)
            {
                dependentPropertiesExist &= Metadata.DeclaringEntityType.FindProperty(dependentProperty.Name) != null;
            }

            var principalPropertiesExist = true;
            foreach (var dependentProperty in Metadata.PrincipalKey.Properties)
            {
                principalPropertiesExist &= Metadata.PrincipalEntityType.FindProperty(dependentProperty.Name) != null;
            }

            return AddRelationship(
                Metadata.PrincipalEntityType,
                Metadata.DeclaringEntityType,
                null,
                null,
                dependentPropertiesExist && _foreignKeyPropertiesConfigurationSource.HasValue ? Metadata.Properties : null,
                principalPropertiesExist && _principalKeyConfigurationSource.HasValue ? Metadata.PrincipalKey.Properties : null,
                _isUniqueConfigurationSource.HasValue ? Metadata.IsUnique : null,
                _isRequiredConfigurationSource.HasValue ? Metadata.IsRequired : null,
                configurationSource,
                configurationSource);
        }

        private InternalRelationshipBuilder MergeConfigurationSourceWith(InternalRelationshipBuilder oldBuilder, ConfigurationSource configurationSource)
        {
            var inverted = oldBuilder.Metadata.DeclaringEntityType != Metadata.DeclaringEntityType;
            Debug.Assert(inverted
                         || (oldBuilder.Metadata.DeclaringEntityType == Metadata.DeclaringEntityType
                             && oldBuilder.Metadata.PrincipalEntityType == Metadata.PrincipalEntityType));
            Debug.Assert(!inverted
                         || (oldBuilder.Metadata.DeclaringEntityType == Metadata.PrincipalEntityType
                             && oldBuilder.Metadata.PrincipalEntityType == Metadata.DeclaringEntityType));

            var targetForeignKeyPropertiesConfigurationSource = inverted
                ? oldBuilder._principalKeyConfigurationSource
                : oldBuilder._foreignKeyPropertiesConfigurationSource;
            var targetPrincipalKeyConfigurationSource = inverted
                ? oldBuilder._foreignKeyPropertiesConfigurationSource
                : oldBuilder._principalKeyConfigurationSource;

            _foreignKeyPropertiesConfigurationSource =
                targetForeignKeyPropertiesConfigurationSource?.Max(_foreignKeyPropertiesConfigurationSource) ?? _foreignKeyPropertiesConfigurationSource;

            _principalKeyConfigurationSource =
                targetPrincipalKeyConfigurationSource?.Max(_principalKeyConfigurationSource) ?? _principalKeyConfigurationSource;
            _principalEndConfigurationSource = oldBuilder._principalEndConfigurationSource;

            _isUniqueConfigurationSource = oldBuilder._isUniqueConfigurationSource?.Max(_isUniqueConfigurationSource) ?? _isUniqueConfigurationSource;
            _isRequiredConfigurationSource = oldBuilder._isRequiredConfigurationSource?.Max(_isRequiredConfigurationSource) ?? _isRequiredConfigurationSource;

            var builder = this;
            if (oldBuilder.Metadata.IsUnique.HasValue && !builder.Metadata.IsUnique.HasValue)
            {
                builder = builder.Unique(oldBuilder.Metadata.IsUnique.Value, configurationSource);
            }
            if (oldBuilder.Metadata.IsRequired.HasValue && !builder.Metadata.IsRequired.HasValue)
            {
                builder = builder.Required(oldBuilder.Metadata.IsRequired.Value, configurationSource);
            }

            return builder;
        }

        private EntityType ResolveType(Type type)
        {
            if (type == Metadata.DeclaringEntityType.ClrType)
            {
                return Metadata.DeclaringEntityType;
            }

            if (type == Metadata.PrincipalEntityType.ClrType)
            {
                return Metadata.PrincipalEntityType;
            }

            throw new ArgumentException(Strings.EntityTypeNotInRelationship(type.FullName, Metadata.DeclaringEntityType.Name, Metadata.PrincipalEntityType.Name));
        }

        private EntityType ResolveType(string name)
        {
            if (name == Metadata.DeclaringEntityType.Name)
            {
                return Metadata.DeclaringEntityType;
            }

            if (name == Metadata.PrincipalEntityType.Name)
            {
                return Metadata.PrincipalEntityType;
            }

            throw new ArgumentException(Strings.EntityTypeNotInRelationship(name, Metadata.DeclaringEntityType.Name, Metadata.PrincipalEntityType.Name));
        }
    }
}
