// Copyright (c) Flecs Hub (https://github.com/flecs-hub). All rights reserved.
// Licensed under the MIT license. See LICENSE file in the Git repository root directory for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using static flecs_hub.flecs;

namespace Flecs;

[PublicAPI]
public unsafe class World
{
    // Relationships
    public Entity EcsIsA => new Entity(this, pinvoke_EcsIsA());

    public Entity EcsDependsOn => new Entity(this, pinvoke_EcsDependsOn());

    public Entity EcsChildOf => new Entity(this, pinvoke_EcsChildOf());

    public Entity EcsSlotOf => new Entity(this, pinvoke_EcsSlotOf());

    // Entity tags
    public Entity EcsPrefab => new Entity(this, pinvoke_EcsPrefab());

    // System tags
    public Entity EcsPreFrame => new Entity(this, pinvoke_EcsPreFrame());

    public Entity EcsOnLoad => new Entity(this, pinvoke_EcsOnLoad());

    public Entity EcsPostLoad => new Entity(this, pinvoke_EcsPostLoad());

    public Entity EcsPreUpdate => new Entity(this, pinvoke_EcsPreUpdate());

    public Entity EcsOnUpdate => new Entity(this, pinvoke_EcsOnUpdate());

    public Entity EcsOnValidate => new Entity(this, pinvoke_EcsOnValidate());

    public Entity EcsPostUpdate => new Entity(this, pinvoke_EcsPostUpdate());

    public Entity EcsPreStore => new Entity(this, pinvoke_EcsPreStore());

    public Entity EcsOnStore => new Entity(this, pinvoke_EcsOnStore());

    public Entity EcsPostFrame => new Entity(this, pinvoke_EcsPostFrame());

    public Entity EcsPhase => new Entity(this, pinvoke_EcsPhase());

    internal static Dictionary<IntPtr, World> Pointers = new();

    internal readonly ecs_world_t* Handle;
    // tags = components without data
    private Dictionary<Type, ulong> _componentIdentifiersByType = new();

    public int ExitCode { get; private set; }

    public World(string[] args)
    {
        var argv = args.Length == 0 ? default : Runtime.CStrings.CStringArray(args);
        Handle = ecs_init_w_args(args.Length, argv);
        Pointers.Add((IntPtr)Handle, this);

        for (var i = 0; i < args.Length; i++)
        {
            Marshal.FreeHGlobal(argv[i]._pointer);
        }
    }

    public int Fini()
    {
        Pointers.Remove((IntPtr)Handle);
        var exitCode = ecs_fini(Handle);
        return exitCode;
    }

    public void RegisterComponent<TComponent>(ComponentHooks? hooks = null)
        where TComponent : unmanaged
    {
        var type = typeof(TComponent);
        var componentName = GetFlecsTypeName(type);
        var componentNameC = (Runtime.CString)componentName;
        var structLayoutAttribute = type.StructLayoutAttribute;
        CheckStructLayout(structLayoutAttribute);
        var structSize = Unsafe.SizeOf<TComponent>();
        var structAlignment = structLayoutAttribute!.Pack;
        if (structAlignment == 0)
        {
            structAlignment = 1;
        }

        ecs_entity_desc_t entityDesc = default;
        entityDesc.name = componentNameC;
        entityDesc.symbol = componentNameC;
        ecs_component_desc_t componentDesc = default;
        componentDesc.entity = ecs_entity_init(Handle, &entityDesc);
        componentDesc.type.size = structSize;
        componentDesc.type.alignment = structAlignment;
        var id = ecs_component_init(Handle, &componentDesc);
        _componentIdentifiersByType[typeof(TComponent)] = id.Data.Data;
        SetHooks(hooks, id);
    }

    public void RegisterTag<TTag>()
        where TTag : unmanaged, ITag
    {
        ecs_entity_desc_t desc = default;
        var type = typeof(TTag);
        var typeName = GetFlecsTypeName<TTag>();
        desc.name = (Runtime.CString)typeName;
        var id = ecs_entity_init(Handle, &desc);
        Debug.Assert(id.Data != 0, "ECS_INVALID_PARAMETER");
        _componentIdentifiersByType[type] = id.Data.Data;
    }

    public void RegisterSystem(
        CallbackIterator callback, Entity phase, string filterExpression, string? name = null)
    {
        RegisterSystem(callback, phase._handle, filterExpression, name);
    }

    public void RegisterSystem(
        CallbackIterator callback, ecs_entity_t phase, string filterExpression, string? name = null)
    {
        ecs_system_desc_t desc = default;
        FillSystemDescriptorCommon(ref desc, callback, phase, name);

        desc.query.filter.expr = (Runtime.CString)filterExpression;
        ecs_system_init(Handle, &desc);
    }

    public void RegisterSystem<TComponent1>(
        CallbackIterator callback, Entity phase, string? name = null)
    {
        ecs_system_desc_t desc = default;
        FillSystemDescriptorCommon(ref desc, callback, phase._handle, name);

        desc.query.filter.expr = (Runtime.CString)GetFlecsTypeName<TComponent1>();
        ecs_system_init(Handle, &desc);
    }

    public void RegisterSystem<TComponent1, TComponent2>(
        CallbackIterator callback, string? name = null)
    {
        ecs_system_desc_t desc = default;
        // desc.query.filter.name = (Runtime.CString)(name ?? callback.Method.Name);
        var phase = EcsOnUpdate;
        FillSystemDescriptorCommon(ref desc, callback, phase._handle, name);

        var componentName1 = GetFlecsTypeName<TComponent1>();
        var componentName2 = GetFlecsTypeName<TComponent2>();
        desc.query.filter.expr = (Runtime.CString)(componentName1 + ", " + componentName2);
        ecs_system_init(Handle, &desc);
    }

    private void FillSystemDescriptorCommon(
        ref ecs_system_desc_t systemDesc, CallbackIterator callback, ecs_entity_t phase, string? name)
    {
        ecs_entity_desc_t entityDesc = default;
        entityDesc.name = (Runtime.CString)(name ?? callback.Method.Name);
        entityDesc.add[0] = phase.Data != 0 ? ecs_pair(EcsDependsOn._handle, phase) : default;
        entityDesc.add[1] = phase;
        systemDesc.entity = ecs_entity_init(Handle, &entityDesc);
        systemDesc.callback.Pointer = &SystemCallback;
        systemDesc.binding_ctx = (void*)CallbacksHelper.CreateSystemCallbackContext(this, callback);
    }

    [UnmanagedCallersOnly]
    private static void SystemCallback(ecs_iter_t* it)
    {
        CallbacksHelper.GetSystemCallbackContext((IntPtr)it->binding_ctx, out var data);

        var iterator = new Iterator(data.World, it);
        data.Callback(iterator);
    }

    public Entity CreateEntity(string name)
    {
        var desc = default(ecs_entity_desc_t);
        desc.name = (Runtime.CString)name;
        var entity = ecs_entity_init(Handle, &desc);
        var result = new Entity(this, entity);
        return result;
    }

    public Entity CreatePrefab(string name)
    {
        var desc = default(ecs_entity_desc_t);
        desc.name = (Runtime.CString)name;
        desc.add[0] = pinvoke_EcsPrefab();

        var entity = ecs_entity_init(Handle, &desc);
        var result = new Entity(this, entity);
        return result;
    }

    public EntityIterator EntityIterator<TComponent>()
        where TComponent : unmanaged, IComponent
    {
        var term = default(ecs_term_t);
        term.id = _componentIdentifiersByType[typeof(TComponent)];
        var iterator = ecs_term_iter(Handle, &term);
        var result = new EntityIterator(this, iterator);
        return result;
    }

    public bool Progress(float deltaTime)
    {
        return ecs_progress(Handle, deltaTime);
    }

    private void SetHooks(ComponentHooks? hooksNullable, ecs_entity_t id)
    {
        if (hooksNullable == null)
        {
            return;
        }

        var hooksDesc = default(ecs_type_hooks_t);
        var hooks = hooksNullable.Value;
        ComponentHooks.Fill(this, ref hooks, &hooksDesc);
        ecs_set_hooks_id(Handle, id, &hooksDesc);
    }

    private static void CheckStructLayout(StructLayoutAttribute? structLayoutAttribute)
    {
        if (structLayoutAttribute == null || structLayoutAttribute.Value == LayoutKind.Auto)
        {
            throw new FlecsException(
                "Component must have a StructLayout attribute with LayoutKind sequential or explicit. This is to ensure that the struct fields are not reorganized by the C# compiler.");
        }
    }

    public string GetFlecsTypeName(Type type)
    {
        return type.FullName!.Replace("+", ".", StringComparison.InvariantCulture);
    }

    public string GetFlecsTypeName<T>()
    {
        return GetFlecsTypeName(typeof(T));
    }

    public Identifier GetIdentifier<T>()
          where T : unmanaged, IEcsComponent
    {
        var type = typeof(T);
        var containsKey = _componentIdentifiersByType.TryGetValue(type, out var value);
        if (!containsKey)
        {
            RegisterComponent<T>();
            value = _componentIdentifiersByType[type];
        }

        var id = default(ecs_id_t);
        id.Data = value;
        return new Identifier(this, id);
    }
}
