using System;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using static flecs_hub.flecs;

namespace flecs;

[PublicAPI]
public readonly unsafe struct EntityIterator
{
    private readonly ecs_world_t* _world;
    internal readonly ecs_iter_t Handle;

    public int Count => Handle.count;

    internal EntityIterator(World world, ecs_iter_t it)
    {
        _world = world.Handle;
        Handle = it;
    }

    public bool TermNext()
    {
        fixed (EntityIterator* @this = &this)
        {
            var handlePointer = &@this->Handle;
            var result = ecs_term_next(handlePointer);
            return result;
        }
    }

    public Span<T> Term<T>(int index)
    {
        fixed (EntityIterator* @this = &this)
        {
            var handlePointer = &@this->Handle;
            var structSize = Marshal.SizeOf<T>();
            var pointer = ecs_term_w_size(handlePointer, (ulong) structSize, index);
            return new Span<T>(pointer, Handle.count);
        }
    }

    public Entity Entity(int index)
    {
        var world = World.Pointers[(IntPtr)_world];
        var result = new Entity(world, Handle.entities[index]);
        return result;
    }
}
