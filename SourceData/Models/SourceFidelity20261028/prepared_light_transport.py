"""Preserve object ray flags and receiver/blocker collection membership when batching.

Atlas jobs may only join sources with identical direct membership in every
linked collection. Clone the collection graph; never expand a receiver set.
"""
RAY_FLAGS = ('visible_camera', 'visible_diffuse', 'visible_glossy',
             'visible_transmission', 'visible_volume_scatter', 'visible_shadow',
             'is_holdout', 'is_shadow_catcher')
TRANSPORT_VERSION = 1


class PreparedLightTransport:
    def __init__(self, source_scene, stage, view_layer, resumed=False):
        import bpy
        self.collections = {}
        self.clones = {}
        self.members = {}
        self.lights = []
        enabled = set()

        def visible(layer, hidden=False):
            hidden = hidden or layer.exclude or layer.collection.hide_render
            if not hidden:
                enabled.add(layer.collection.name)
            for child in layer.children:
                visible(child, hidden)

        visible(view_layer.layer_collection)

        def collect(collection):
            if collection.name in self.collections:
                return
            self.collections[collection.name] = collection
            self.members[collection.name] = {
                obj.name: collection.collection_objects[i].light_linking.link_state
                for i, obj in enumerate(collection.objects)}
            for obj in collection.objects:
                if obj.instance_type != 'NONE':
                    raise ValueError('Linked instancer needs explicit evaluated membership: ' + obj.name)
            for child in collection.children:
                collect(child)

        for obj in source_scene.objects:
            if obj.type != 'LIGHT' or obj.hide_render or not any(c.name in enabled for c in obj.users_collection):
                continue
            self.lights.append(obj)
            for key in ('receiver_collection', 'blocker_collection'):
                collection = getattr(obj.light_linking, key)
                if collection:
                    collect(collection)
        self.keys = sorted(self.collections)

        def clone(collection):
            if collection.name in self.clones:
                return self.clones[collection.name]
            result = bpy.data.collections.get('SOURCE_LINK_' + collection.name) if resumed else None
            if result and result.get('source_link_collection') != collection.name:
                raise ValueError('Prepared light-link collection identity collision: ' + collection.name)
            if not result:
                result = bpy.data.collections.new('SOURCE_LINK_' + collection.name)
                result['source_link_collection'] = collection.name
            self.clones[collection.name] = result
            result.hide_render = collection.hide_render
            result.hide_viewport = collection.hide_viewport
            for i, child in enumerate(collection.children):
                copied_child = clone(child)
                if copied_child.name not in result.children:
                    result.children.link(copied_child)
                index = list(result.children).index(self.clones[child.name])
                result.collection_children[index].light_linking.link_state = collection.collection_children[i].light_linking.link_state
            return result

        for collection in self.collections.values():
            clone(collection)

    def signature(self, source_name):
        return tuple(self.members[key].get(source_name, '') for key in self.keys)

    def bind_receiver(self, obj, source_names):
        signatures = {self.signature(name) for name in source_names}
        if len(signatures) != 1:
            raise ValueError('Cannot merge different light-link membership: ' + obj.name)
        for key, state in zip(self.keys, next(iter(signatures))):
            if not state:
                continue
            collection = self.clones[key]
            collection.objects.link(obj)
            index = list(collection.objects).index(obj)
            collection.collection_objects[index].light_linking.link_state = state

    def copy_light(self, source, stage):
        import bpy
        result = bpy.data.objects.new('SOURCE_' + source.name, source.data.copy())
        stage.collection.objects.link(result)
        result.matrix_world = source.matrix_world.copy()
        for flag in RAY_FLAGS:
            setattr(result, flag, getattr(source, flag))
        for key in ('receiver_collection', 'blocker_collection'):
            collection = getattr(source.light_linking, key)
            setattr(result.light_linking, key, self.clones[collection.name] if collection else None)
        return result
